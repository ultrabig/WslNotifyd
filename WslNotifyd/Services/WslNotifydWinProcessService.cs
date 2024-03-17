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
        private readonly HashSet<uint> _notificationIds = [];
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private readonly ManualResetEventSlim _stopped = new ManualResetEventSlim(true);
        private Task? _stopTask;
        private CancellationTokenSource? _cancelStop;
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
            _lifetime.ApplicationStarted.Register(startTcs.SetResult);
            var stopTcs = new TaskCompletionSource();
            stoppingToken.Register(stopTcs.SetResult);

            var completedTask = await Task.WhenAny(startTcs.Task, stopTcs.Task);
            if (completedTask == stopTcs.Task)
            {
                return;
            }

            var addressFeature = _server.Features.GetRequiredFeature<IServerAddressesFeature>();
            var address = addressFeature.Addresses.ElementAt(0);
            _psi.ArgumentList.Add(address);

            _lifetime.ApplicationStopping.Register(HandleStopping);

            while (!_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                try
                {
                    // wrap in Task for starting up to progress
                    await Task.Run(() =>
                    {
                        _running.Wait(_lifetime.ApplicationStopping);
                        _stopped.Reset();
                    }, _lifetime.ApplicationStopping);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                await RunProcess();
            }
        }

        public async Task RequestStart(uint id, CancellationToken cancellationToken = default)
        {
            if (_lifetime.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }
            _cancelStop?.Cancel();
            _notificationIds.Add(id);
            if (_running.IsSet)
            {
                return;
            }
            await Task.Run(() =>
            {
                _stopped.Wait(cancellationToken);
            }, cancellationToken);
            _running.Set();
        }

        public void NotificationHandled(uint id)
        {
            _notificationIds.Remove(id);
            if (_notificationIds.Count > 0)
            {
                return;
            }
            _cancelStop?.Cancel();
            _cancelStop = new CancellationTokenSource();
            // TODO: make the timeout to configurable
            _stopTask = Task.Delay(10000, _cancelStop.Token).ContinueWith(async (ta) =>
            {
                if (ta.IsCanceled)
                {
                    return;
                }
                if (ta.IsFaulted)
                {
                    throw ta.Exception;
                }
                _logger.LogInformation("shutting down subprocess");
                await Shutdown(false);
            });
        }

        private async Task RunProcess()
        {
            _proc = Process.Start(_psi);
            if (_proc == null)
            {
                _logger.LogError("error in executing subprocess");
                _lifetime.StopApplication();
                return;
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

            await _proc.WaitForExitAsync();
            _proc.Dispose();
            _proc = null;
        }

        private async Task KillWait(CancellationToken cancellationToken = default)
        {
            _proc?.Kill(true);
            await Task.Run(() =>
            {
                _stopped.Wait(cancellationToken);
            }, cancellationToken);
        }

        private async Task Shutdown(bool forceKillAndReturn, CancellationToken cancellationToken = default)
        {
            if (_proc == null || _proc.HasExited)
            {
                return;
            }
            if (forceKillAndReturn)
            {
                _logger.LogWarning("force kill");
                _proc?.Kill(true);
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
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                OnShutdownRequest.Invoke();
                // TODO: make the timeout to configurable
                cts.CancelAfter(5000);
                await Task.Run(() =>
                {
                    _stopped.Wait(cts.Token);
                }, cts.Token);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogInformation(ex, "fall back to force kill");
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
            _notificationIds.Clear();
            _running.Reset();
            _stopped.Set();
        }
    }
}
