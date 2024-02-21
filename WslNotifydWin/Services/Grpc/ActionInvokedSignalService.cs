using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.Services.Grpc.Base;

namespace WslNotifydWin.Services.Grpc
{
    class ActionInvokedSignalService : ClientStreamingService<ActionInvokedRequest, ActionInvokedReply>
    {
        public ActionInvokedSignalService(ILogger<ClientStreamingService<ActionInvokedRequest, ActionInvokedReply>> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncClientStreamingCall<ActionInvokedRequest, ActionInvokedReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.ActionInvoked(cancellationToken: cancellationToken));
        }

        protected override Task RegisterEventHandlerAsync()
        {
            notif.OnAction += HandleClose;
            return Task.CompletedTask;
        }

        protected override Task UnregisterEventHandlerAsync()
        {
            notif.OnAction -= HandleClose;
            return Task.CompletedTask;
        }

        private void HandleClose((uint, string) arg)
        {
            if (streamingCall != null)
            {
                streamingCall.RequestStream.WriteAsync(new ActionInvokedRequest()
                {
                    NotificationId = arg.Item1,
                    ActionKey = arg.Item2,
                }).Wait();
            }
        }
    }
}
