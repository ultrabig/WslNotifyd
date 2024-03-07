using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.GrpcServices.Base;

namespace WslNotifydWin.GrpcServices
{
    class ActionInvokedSignalService : ClientStreamingService<ActionInvokedRequest, ActionInvokedReply, ActionInvokedSignalService>
    {
        public ActionInvokedSignalService(ILogger<ActionInvokedSignalService> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
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

        private void HandleClose((uint id, string actionKey) arg)
        {
            if (streamingCall != null)
            {
                streamingCall.RequestStream.WriteAsync(new ActionInvokedRequest()
                {
                    NotificationId = arg.id,
                    ActionKey = arg.actionKey,
                }).Wait();
            }
        }
    }
}
