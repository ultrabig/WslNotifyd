using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.Services
{
    class ActionInvokedSignalService : IHostedService
    {
        private readonly ILogger<ActionInvokedSignalService> _logger;
        private readonly Notifier.NotifierClient _client;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly Notification _notif;

        public ActionInvokedSignalService(ILogger<ActionInvokedSignalService> logger, Notifier.NotifierClient notifierClient, IHostApplicationLifetime lifetime, Notification notification)
        {
            _logger = logger;
            _client = notifierClient;
            _lifetime = lifetime;
            _notif = notification;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _notif.OnAction += HandleOnAction;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _notif.OnAction -= HandleOnAction;
            return Task.CompletedTask;
        }

        private void HandleOnAction((uint id, string actionKey) arg)
        {
            _client.ActionInvoked(new ActionInvokedRequest()
            {
                NotificationId = arg.id,
                ActionKey = arg.actionKey,
            }, cancellationToken: _lifetime.ApplicationStopping);
        }
    }
}
