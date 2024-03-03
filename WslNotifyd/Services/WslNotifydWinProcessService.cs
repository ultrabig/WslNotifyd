using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;

namespace WslNotifyd.Services
{
    public class WslNotifydWinProcessService(ILogger<WslNotifydWinProcessService> logger, IHostApplicationLifetime lifetime, IServer server, ProcessStartInfo psi, byte[]? stdin = null) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var startTcs = new TaskCompletionSource();
            lifetime.ApplicationStarted.Register(startTcs.SetResult);
            var stopTcs = new TaskCompletionSource();
            stoppingToken.Register(stopTcs.SetResult);

            var completedTask = await Task.WhenAny(startTcs.Task, stopTcs.Task);
            if (completedTask == stopTcs.Task)
            {
                return;
            }

            var addressFeature = server.Features.GetRequiredFeature<IServerAddressesFeature>();
            var address = addressFeature.Addresses.ElementAt(0);
            psi.ArgumentList.Add(address);

            using var proc = Process.Start(psi);
            if (proc == null)
            {
                logger.LogError("error in executing subprocess");
                lifetime.StopApplication();
                return;
            }

            lifetime.ApplicationStopping.Register(() =>
            {
                proc.Kill(true);
            });

            proc.EnableRaisingEvents = true;
            proc.Exited += HandleExited;
            if (psi.RedirectStandardOutput)
            {
                proc.OutputDataReceived += HandleDataReceived;
                proc.BeginOutputReadLine();
            }
            if (psi.RedirectStandardError)
            {
                proc.ErrorDataReceived += HandleErrorReceived;
                proc.BeginErrorReadLine();
            }
            if (psi.RedirectStandardInput)
            {
                if (stdin != null)
                {
                    proc.StandardInput.BaseStream.Write(stdin);
                }
                proc.StandardInput.Close();
                proc.StandardInput.Dispose();
            }

            await proc.WaitForExitAsync();
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
