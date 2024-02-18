using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WslNotifyd.Services
{
    public class ProcessService(ILogger<ProcessService> logger, ProcessStartInfo psi, IHostApplicationLifetime lifetime) : IHostedService, IAsyncDisposable
    {
        Process? _proc = null;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _proc = Process.Start(psi);
            if (_proc != null)
            {
                _proc.EnableRaisingEvents = true;
                _proc.Exited += HandleExited;
                if (psi.RedirectStandardOutput)
                {
                    _proc.OutputDataReceived += HandleDataReceived;
                    _proc.BeginOutputReadLine();
                }
                if (psi.RedirectStandardError)
                {
                    _proc.ErrorDataReceived += HandleErrorReceived;
                    _proc.BeginErrorReadLine();
                }
                _proc.StandardInput.Close();
                _proc.StandardInput.Dispose();
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            if (_proc != null)
            {
                logger.LogInformation("HasExited: {0}", _proc.HasExited);
                if (_proc.HasExited)
                {
                    logger.LogInformation("ExitCode: {0}", _proc.ExitCode);
                }
                _proc.Kill(true);
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_proc != null)
            {
                _proc.Exited -= HandleExited;
                _proc.Dispose();
                _proc = null;
            }
            return ValueTask.CompletedTask;
        }

        private void HandleDataReceived(object? sender, DataReceivedEventArgs e)
        {
            logger.LogInformation("stdout: {0}", e.Data);
        }

        private void HandleErrorReceived(object? sender, DataReceivedEventArgs e)
        {
            logger.LogInformation("stderr: {0}", e.Data);
        }

        private void HandleExited(object? sender, EventArgs e)
        {
            lifetime.StopApplication();
        }
    }
}
