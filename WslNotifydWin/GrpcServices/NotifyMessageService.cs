using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.GrpcServices.Base;

namespace WslNotifydWin.GrpcServices
{
    class NotifyMessageService : DuplexStreamingService<NotifyRequest, NotifyReply, NotifyMessageService>
    {
        public NotifyMessageService(ILogger<NotifyMessageService> logger, Notifier.NotifierClient notifierClient, Notification notification) : base(logger, notifierClient, notification)
        {
        }

        protected override Task<AsyncDuplexStreamingCall<NotifyRequest, NotifyReply>> CreateStreamingCallAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(client.Notify());
        }

        protected override async Task<NotifyRequest> HandleResponseAsync(NotifyReply response, CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, byte[]>();
            foreach (var (k, v) in response.NotificationData)
            {
                data[k] = v.ToByteArray();
            }
            var id = await notif.NotifyAsync(response.NotificationXml, response.NotificationId, data);
            var req = new NotifyRequest()
            {
                Success = true,
                SerialId = response.SerialId,
                NotificationId = id,
            };
            return req;
        }

        protected override Task<NotifyRequest> HandleErrorAsync(NotifyReply response, Exception ex, CancellationToken cancellationToken)
        {
            var req = new NotifyRequest()
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
