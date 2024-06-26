using Google.Protobuf;
using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Logging;
using WslNotifyd.DBus;
using WslNotifyd.Services;

namespace WslNotifyd.GrpcServices
{
    internal class NotifierService : Notifier.NotifierBase
    {
        private readonly ILogger<NotifierService> _logger;
        private readonly Notifications _notifications;
        private readonly WslNotifydWinProcessService _processService;
        private uint _notifySerial = 0;
        private uint _closeNotificationSerial = 0;
        public NotifierService(ILogger<NotifierService> logger, Notifications notifications, WslNotifydWinProcessService processService)
        {
            _logger = logger;
            _notifications = notifications;
            _processService = processService;
        }

        public override async Task CloseNotification(IAsyncStreamReader<CloseNotificationRequest> requestStream, IServerStreamWriter<CloseNotificationReply> responseStream, ServerCallContext context)
        {
            var watcher = new EventWatcher<CloseNotificationRequest>();
            async Task HandleCloseNotification(Notifications sender, Notifications.CloseNotificationEventArgs args)
            {
                var serial = Interlocked.Increment(ref _closeNotificationSerial);
                var reply = new CloseNotificationReply()
                {
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                var tcs = new TaskCompletionSource();
                using var reg = context.CancellationToken.Register(() => tcs.TrySetCanceled());
                void handler(CloseNotificationRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        if (req.Success)
                        {
                            tcs.TrySetResult();
                        }
                        else
                        {
                            tcs.TrySetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                try
                {
                    await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
                }
                finally
                {
                    watcher.OnEventOccured -= handler;
                }
            }
            _notifications.OnCloseNotification += HandleCloseNotification;
            try
            {
                if (!context.CancellationToken.IsCancellationRequested)
                {
                    await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                    {
                        watcher.FireEvent(request);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogInformation(ex, "connection stopped");
            }
            finally
            {
                _notifications.OnCloseNotification -= HandleCloseNotification;
            }
        }

        public override async Task Notify(IAsyncStreamReader<NotifyRequest> requestStream, IServerStreamWriter<NotifyReply> responseStream, ServerCallContext context)
        {
            var watcher = new EventWatcher<NotifyRequest>();
            async Task<uint> handleNotify(Notifications sender, Notifications.NotifyEventArgs args)
            {
                var serial = Interlocked.Increment(ref _notifySerial);
                var reply = new NotifyReply()
                {
                    NotificationXml = args.NotificationXml,
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                foreach (var (k, v) in args.NotificationData)
                {
                    reply.NotificationData.Add(k, ByteString.CopyFrom(v));
                }
                var tcs = new TaskCompletionSource<uint>();
                using var reg = context.CancellationToken.Register(() => tcs.TrySetCanceled());
                void handler(NotifyRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        if (req.Success)
                        {
                            tcs.TrySetResult(req.NotificationId);
                        }
                        else
                        {
                            tcs.TrySetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                try
                {
                    await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
                }
                finally
                {
                    watcher.OnEventOccured -= handler;
                }
                return tcs.Task.Result;
            }
            _notifications.OnNotify += handleNotify;
            try
            {
                if (!context.CancellationToken.IsCancellationRequested)
                {
                    await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                    {
                        watcher.FireEvent(request);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogInformation(ex, "connection stopped");
            }
            finally
            {
                _notifications.OnNotify -= handleNotify;
            }
        }

        public override Task<NotificationClosedReply> NotificationClosed(NotificationClosedRequest request, ServerCallContext context)
        {
            _notifications.FireOnClose(request.NotificationId, request.Reason);
            return Task.FromResult(new NotificationClosedReply());
        }

        public override Task<ActionInvokedReply> ActionInvoked(ActionInvokedRequest request, ServerCallContext context)
        {
            _notifications.FireOnAction(request.NotificationId, request.ActionKey);
            return Task.FromResult(new ActionInvokedReply());
        }

        public override Task<NotificationRepliedReply> NotificationReplied(NotificationRepliedRequest request, ServerCallContext context)
        {
            _notifications.FireOnReply(request.NotificationId, request.Text);
            return Task.FromResult(new NotificationRepliedReply());
        }

        public override async Task Shutdown(ShutdownRequest request, IServerStreamWriter<ShutdownReply> responseStream, ServerCallContext context)
        {
            void HandleShutdownRequest()
            {
                responseStream.WriteAsync(new ShutdownReply()).Wait();
            }
            _processService.OnShutdownRequest += HandleShutdownRequest;
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                _processService.OnShutdownRequest -= HandleShutdownRequest;
            }
        }

        public override Task<MessageDurationReply> MessageDurationChanged(MessageDurationRequest request, ServerCallContext context)
        {
            _notifications.NotificationDuration = request.MessageDuration;
            return Task.FromResult(new MessageDurationReply());
        }

        private class EventWatcher<T>
        {
            public event Action<T>? OnEventOccured;

            public void FireEvent(T req)
            {
                OnEventOccured?.Invoke(req);
            }
        }
    }
}
