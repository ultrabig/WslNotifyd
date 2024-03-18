using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.Services
{
    class NotificationClosedSignalService : IHostedService
    {
        private readonly ILogger<NotificationClosedSignalService> _logger;
        private readonly Notifier.NotifierClient _client;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Notification _notif;

        public NotificationClosedSignalService(ILogger<NotificationClosedSignalService> logger, Notifier.NotifierClient notifierClient, IHostApplicationLifetime lifetime, Notification notification)
        {
            _logger = logger;
            _client = notifierClient;
            _lifetime = lifetime;
            _notif = notification;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _notif.OnClose += HandleOnClose;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _notif.OnClose -= HandleOnClose;
            return Task.CompletedTask;
        }

        private void HandleOnClose((uint id, uint reason) arg)
        {
            _client.NotificationClosed(new NotificationClosedRequest()
            {
                NotificationId = arg.id,
                Reason = arg.reason,
            }, cancellationToken: _lifetime.ApplicationStopping);
        }
    }
}
