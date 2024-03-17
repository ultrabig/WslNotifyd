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
        private volatile uint _notifySerial = 0;
        private volatile uint _closeNotificationSerial = 0;
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
                var serial = _closeNotificationSerial;
                Interlocked.Increment(ref _closeNotificationSerial);
                var reply = new CloseNotificationReply()
                {
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                var tcs = new TaskCompletionSource();
                void handler(CloseNotificationRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        watcher.OnEventOccured -= handler;
                        if (req.Success)
                        {
                            tcs.SetResult();
                        }
                        else
                        {
                            tcs.SetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
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
                var serial = _notifySerial;
                Interlocked.Increment(ref _notifySerial);
                var reply = new NotifyReply()
                {
                    NotificationXml = args.NotificationXml,
                    NotificationId = args.NotificationId,
                    SerialId = serial,
                };
                foreach (var (k, v) in args.NotificionData)
                {
                    reply.NotificationData.Add(k, ByteString.CopyFrom(v));
                }
                var tcs = new TaskCompletionSource<uint>();
                void handler(NotifyRequest req)
                {
                    if (req.SerialId == serial)
                    {
                        watcher.OnEventOccured -= handler;
                        if (req.Success)
                        {
                            tcs.SetResult(req.NotificationId);
                        }
                        else
                        {
                            tcs.SetException(new Exception(req.Error.ErrorMessage));
                        }
                    }
                }
                watcher.OnEventOccured += handler;
                await Task.WhenAll(tcs.Task, responseStream.WriteAsync(reply, context.CancellationToken));
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

        public override async Task<NotificationClosedReply> NotificationClosed(IAsyncStreamReader<NotificationClosedRequest> requestStream, ServerCallContext context)
        {
            try
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    _notifications.FireOnClose(request.NotificationId, request.Reason);
                }
            }
            catch (IOException ex)
            {
                _logger.LogInformation(ex, "connection stopped");
            }
            return new NotificationClosedReply();
        }

        public override async Task<ActionInvokedReply> ActionInvoked(IAsyncStreamReader<ActionInvokedRequest> requestStream, ServerCallContext context)
        {
            try
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    _notifications.FireOnAction(request.NotificationId, request.ActionKey);
                }
            }
            catch (IOException ex)
            {
                _logger.LogInformation(ex, "connection stopped");
            }
            return new ActionInvokedReply();
        }

        public override async Task<NotificationRepliedReply> NotificationReplied(IAsyncStreamReader<NotificationRepliedRequest> requestStream, ServerCallContext context)
        {
            try
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    _notifications.FireOnReply(request.NotificationId, request.Text);
                }
            }
            catch (IOException ex)
            {
                _logger.LogInformation(ex, "connection stopped");
            }
            return new NotificationRepliedReply();
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
