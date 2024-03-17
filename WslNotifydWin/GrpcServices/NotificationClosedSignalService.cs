using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.GrpcServices.Base;


namespace WslNotifydWin.GrpcServices
{
    class NotificationClosedSignalService : ClientStreamingService<NotificationClosedRequest, NotificationClosedReply, NotificationClosedSignalService>
    {
        public NotificationClosedSignalService(ILogger<NotificationClosedSignalService> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncClientStreamingCall<NotificationClosedRequest, NotificationClosedReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.NotificationClosed(cancellationToken: cancellationToken));
        }

        protected override Task RegisterEventHandlerAsync()
        {
            notif.OnClose += HandleClose;
            return Task.CompletedTask;
        }

        protected override Task UnregisterEventHandlerAsync()
        {
            notif.OnClose -= HandleClose;
            return Task.CompletedTask;
        }

        private void HandleClose((uint id, uint reason) arg)
        {
            WriteStream(new NotificationClosedRequest()
            {
                NotificationId = arg.id,
                Reason = arg.reason,
            }).Wait();
        }
    }
}
