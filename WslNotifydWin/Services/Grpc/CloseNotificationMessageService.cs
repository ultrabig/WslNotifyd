using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.Services.Grpc.Base;

namespace WslNotifydWin.Services.Grpc
{
    class CloseNotificationMessageService : DuplexStreamingService<CloseNotificationRequest, CloseNotificationReply, CloseNotificationMessageService>
    {
        public CloseNotificationMessageService(ILogger<CloseNotificationMessageService> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncDuplexStreamingCall<CloseNotificationRequest, CloseNotificationReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.CloseNotification());
        }

        protected override async Task<CloseNotificationRequest> HandleResponseAsync(CloseNotificationReply response, CancellationToken cancellationToken)
        {
            var id = response.NotificationId;
            await notif.CloseNotificationAsync(id);
            var req = new CloseNotificationRequest()
            {
                Success = true,
                SerialId = response.SerialId,
            };
            return req;
        }

        protected override Task<CloseNotificationRequest> HandleErrorAsync(CloseNotificationReply response, Exception ex, CancellationToken cancellationToken)
        {
            var req = new CloseNotificationRequest()
            {
                Success = false,
                SerialId = response.SerialId,
                Error = new ClientError()
                {
                    ErrorMessage = ex.ToString(),
                },
            };
            return Task.FromResult(req);
        }
    }
}
