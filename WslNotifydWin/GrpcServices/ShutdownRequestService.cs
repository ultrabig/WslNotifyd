using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WslNotifydWin.GrpcServices
{
    class ShutdownRequestService : BackgroundService
    {
        private readonly ILogger<ShutdownRequestService> _logger;
        private readonly Notifier.NotifierClient _client;
        private readonly IHostApplicationLifetime _lifetime;

        public ShutdownRequestService(ILogger<ShutdownRequestService> logger, Notifier.NotifierClient notifierClient, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _client = notifierClient;
            _lifetime = lifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var streaming = _client.Shutdown(new ShutdownRequest(), cancellationToken: stoppingToken);
                var next = await streaming.ResponseStream.MoveNext(stoppingToken);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                return;
            }
            finally
            {
                _logger.LogInformation("received shutdown request");
                _lifetime.StopApplication();
            }
        }
    }
}
