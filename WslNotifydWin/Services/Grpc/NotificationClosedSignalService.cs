using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.Services.Grpc.Base;


namespace WslNotifydWin.Services.Grpc
{
    class NotificationClosedSignalService : ClientStreamingService<NotificationClosedRequest, NotificationClosedReply>
    {
        public NotificationClosedSignalService(ILogger<ClientStreamingService<NotificationClosedRequest, NotificationClosedReply>> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncClientStreamingCall<NotificationClosedRequest, NotificationClosedReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.NotificationClosed());
        }

        protected override Task RegisterEventHandlerAsync(CancellationToken cancellationToken)
        {
            notif.OnClose += HandleClose;
            return Task.CompletedTask;
        }

        protected override Task UnregisterEventHandlerAsync(CancellationToken cancellationToken)
        {
            notif.OnClose -= HandleClose;
            return Task.CompletedTask;
        }

        private void HandleClose((uint, uint) arg)
        {
            if (streamingCall != null)
            {
                streamingCall.RequestStream.WriteAsync(new NotificationClosedRequest()
                {
                    NotificationId = arg.Item1,
                    Reason = arg.Item2,
                }).Wait();
            }
        }
    }
}
