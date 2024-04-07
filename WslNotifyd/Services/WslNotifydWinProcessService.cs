using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace WslNotifyd.Services
{
    public class WslNotifydWinProcessService : BackgroundService, IDisposable
    {
        private readonly ILogger<WslNotifydWinProcessService> _logger;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly IServer _server;
        private readonly ProcessStartInfo _psi;
        private readonly byte[]? _stdin;
        private readonly object _lockProcess = new object();
        private readonly ManualResetEventSlim _runnable = new ManualResetEventSlim();
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private Process? _proc;
        public event Action? OnShutdownRequest;

        public WslNotifydWinProcessService(ILogger<WslNotifydWinProcessService> logger, IHostApplicationLifetime lifetime, IServer server, ProcessStartInfo psi, byte[]? stdin = null)
        {
            _logger = logger;
            _lifetime = lifetime;
            _server = server;
            _psi = psi;
            _stdin = stdin;
        }

        public override void Dispose()
        {
            try
            {
                _runnable.Dispose();
                _running.Dispose();

                _proc?.Dispose();
                _proc = null;
            }
            finally
            {
                base.Dispose();
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var startTcs = new TaskCompletionSource();
            var startReg = _lifetime.ApplicationStarted.Register(startTcs.SetResult);
            var stopTcs = new TaskCompletionSource();
            var stopReg = stoppingToken.Register(stopTcs.SetResult);

            var completedTask = await Task.WhenAny(startTcs.Task, stopTcs.Task);
            startReg.Dispose();
            stopReg.Dispose();
            if (completedTask == stopTcs.Task)
            {
                return;
            }

            var addressFeature = _server.Features.GetRequiredFeature<IServerAddressesFeature>();
            var address = addressFeature.Addresses.ElementAt(0);
            _psi.ArgumentList.Add(address);

            using var reg = _lifetime.ApplicationStopping.Register(HandleStopping);

            while (!_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                try
                {
                    // wrap in Task for starting up to progress
                    await Task.Run(() =>
                    {
                        _runnable.Wait(_lifetime.ApplicationStopping);
                    }, _lifetime.ApplicationStopping);
                    await RunProcess(_lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public void RequestStart()
        {
            if (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }
            lock (_lockProcess)
            {
                _runnable.Set();
            }
        }

        public async Task WaitForExitAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                _running.Wait(cancellationToken);
            }, cancellationToken);
            if (_proc == null)
            {
                throw new Exception("error in executing subprocess");
            }
            await _proc.WaitForExitAsync(cancellationToken);
        }

        private async Task RunProcess(CancellationToken cancellationToken = default)
        {
            _proc = Process.Start(_psi);
            if (_proc == null)
            {
                _logger.LogError("error in executing subprocess");
                lock (_lockProcess)
                {
                    _runnable.Reset();
                    _running.Reset();
                }
                _lifetime.StopApplication();
                return;
            }
            lock (_lockProcess)
            {
                _running.Set();
            }
            _logger.LogInformation("process started");

            _proc.EnableRaisingEvents = true;
            _proc.Exited += HandleExited;
            if (_psi.RedirectStandardOutput)
            {
                _proc.OutputDataReceived += HandleDataReceived;
                _proc.BeginOutputReadLine();
            }
            if (_psi.RedirectStandardError)
            {
                _proc.ErrorDataReceived += HandleErrorReceived;
                _proc.BeginErrorReadLine();
            }
            if (_psi.RedirectStandardInput)
            {
                if (_stdin != null)
                {
                    _proc.StandardInput.BaseStream.Write(_stdin);
                }
                _proc.StandardInput.Close();
                _proc.StandardInput.Dispose();
            }

            await _proc.WaitForExitAsync(cancellationToken);
            _proc.Dispose();
            _proc = null;
        }

        private async Task KillWait(CancellationToken cancellationToken = default)
        {
            if (_proc == null || _proc.HasExited)
            {
                return;
            }
            _proc.Kill(true);
            await _proc.WaitForExitAsync(cancellationToken);
        }

        private async Task Shutdown(bool forceKillAndReturn, CancellationToken cancellationToken = default)
        {
            if (_proc == null || _proc.HasExited)
            {
                return;
            }
            if (forceKillAndReturn)
            {
                _logger.LogInformation("force kill");
                _proc.Kill(true);
                return;
            }

            if (OnShutdownRequest == null || OnShutdownRequest.GetInvocationList().Length == 0)
            {
                _logger.LogWarning("cannot gracefully shutdown, force kill");
                await KillWait(cancellationToken);
                return;
            }

            try
            {
                _logger.LogInformation("gracefully shut down");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                OnShutdownRequest.Invoke();
                if (_proc == null || _proc.HasExited)
                {
                    return;
                }
                // TODO: make the timeout to be configurable
                cts.CancelAfter(5000);
                await _proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "fall back to force kill");
                await KillWait(cancellationToken);
            }
        }

        private void HandleStopping()
        {
            Shutdown(false).Wait();
        }

        private void HandleDataReceived(object? sender, DataReceivedEventArgs e)
        {
            _logger.LogInformation("stdout: {0}", e.Data);
        }

        private void HandleErrorReceived(object? sender, DataReceivedEventArgs e)
        {
            _logger.LogInformation("stderr: {0}", e.Data);
        }

        private void HandleExited(object? sender, EventArgs e)
        {
            _logger.LogInformation("process exited");
            lock (_lockProcess)
            {
                _runnable.Reset();
                _running.Reset();
            }
        }
    }
}
