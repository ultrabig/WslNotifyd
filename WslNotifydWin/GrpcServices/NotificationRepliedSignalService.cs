using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.GrpcServices.Base;
namespace WslNotifydWin.GrpcServices
{
    class NotificationRepliedSignalService : ClientStreamingService<NotificationRepliedRequest, NotificationRepliedReply, NotificationRepliedSignalService>
    {
        public NotificationRepliedSignalService(ILogger<NotificationRepliedSignalService> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncClientStreamingCall<NotificationRepliedRequest, NotificationRepliedReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.NotificationReplied(cancellationToken: cancellationToken));
        }

        protected override Task RegisterEventHandlerAsync()
        {
            notif.OnReply += HandleReplied;
            return Task.CompletedTask;
        }

        protected override Task UnregisterEventHandlerAsync()
        {
            notif.OnReply -= HandleReplied;
            return Task.CompletedTask;
        }

        private void HandleReplied((uint id, string text) arg)
        {
            WriteStream(new NotificationRepliedRequest()
            {
                NotificationId = arg.id,
                Text = arg.text,
            }).Wait();
        }
    }
}
