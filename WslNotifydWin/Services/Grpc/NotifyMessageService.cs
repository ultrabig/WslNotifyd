using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;
using WslNotifydWin.Services.Grpc.Base;

namespace WslNotifydWin.Services.Grpc
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
            var actions = response.Actions.ToArray();
            var hints = new Dictionary<string, object>();
            foreach (var (k, v) in response.Hints)
            {
                if (v.ValCase == NotificationHintVariant.ValOneofCase.ByteVal)
                {
                    hints.Add(k, v.ByteVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.BoolVal)
                {
                    hints.Add(k, v.BoolVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.ShortVal)
                {
                    hints.Add(k, v.ShortVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.UshortVal)
                {
                    hints.Add(k, v.UshortVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.IntVal)
                {
                    hints.Add(k, v.IntVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.UintVal)
                {
                    hints.Add(k, v.UintVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.LongVal)
                {
                    hints.Add(k, v.LongVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.UlongVal)
                {
                    hints.Add(k, v.UlongVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.FloatVal)
                {
                    hints.Add(k, v.FloatVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.DoubleVal)
                {
                    hints.Add(k, v.DoubleVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.StringVal)
                {
                    hints.Add(k, v.StringVal);
                }
                else if (v.ValCase == NotificationHintVariant.ValOneofCase.BytesVal)
                {
                    var bs = new byte[v.BytesVal.Length];
                    v.BytesVal.CopyTo(bs, 0);
                    hints.Add(k, bs);
                }
            }
            var id = await notif.NotifyAsync(response.AppName, response.ReplacesId, response.AppIcon, response.Summary, response.Body, actions, hints, response.ExpireTimeout, response.NotificationId);
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
