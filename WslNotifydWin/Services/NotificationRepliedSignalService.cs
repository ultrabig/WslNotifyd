using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.Services
{
    class NotificationRepliedSignalService : IHostedService
    {
        private readonly ILogger<NotificationRepliedSignalService> _logger;
        private readonly Notifier.NotifierClient _client;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Notification _notif;

        public NotificationRepliedSignalService(ILogger<NotificationRepliedSignalService> logger, Notifier.NotifierClient notifierClient, IHostApplicationLifetime lifetime, Notification notification)
        {
            _logger = logger;
            _client = notifierClient;
            _lifetime = lifetime;
            _notif = notification;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _notif.OnReply += HandleOnReply;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _notif.OnReply -= HandleOnReply;
            return Task.CompletedTask;
        }

        private void HandleOnReply((uint id, string text) arg)
        {
            _client.NotificationReplied(new NotificationRepliedRequest()
            {
                NotificationId = arg.id,
                Text = arg.text,
            }, cancellationToken: _lifetime.ApplicationStopping);
        }
    }
}
