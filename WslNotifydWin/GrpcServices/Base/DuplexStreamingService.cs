using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.GrpcServices.Base
{
    abstract class DuplexStreamingService<TRequest, TResponse, TService> : IHostedService, IAsyncDisposable where TRequest : class
    {
        protected readonly ILogger<TService> logger;
        protected readonly Notifier.NotifierClient client;
        protected readonly Notification notif;
        protected AsyncDuplexStreamingCall<TRequest, TResponse>? streamingCall;
        protected uint serialId = 0;
        private Task? _readingTask;
        private CancellationTokenSource? _cts;

        public DuplexStreamingService(
            ILogger<TService> logger,
            Notifier.NotifierClient notifierClient,
            Notification notification)
        {
            this.logger = logger;
            client = notifierClient;
            notif = notification;
        }

        protected abstract Task<AsyncDuplexStreamingCall<TRequest, TResponse>> CreateStreamingCallAsync(CancellationToken cancellationToken);

        protected abstract Task<TRequest> HandleResponseAsync(TResponse response, CancellationToken cancellationToken);

        protected abstract Task<TRequest> HandleErrorAsync(TResponse response, Exception ex, CancellationToken cancellationToken);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            streamingCall = await CreateStreamingCallAsync(cancellationToken);
            _cts = new CancellationTokenSource();
            _readingTask = Task.Run(async () =>
            {
                await foreach (var response in streamingCall.ResponseStream.ReadAllAsync(_cts.Token))
                {
                    TRequest? req = null;
                    try
                    {
                        req = await HandleResponseAsync(response, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        var errorReq = await HandleErrorAsync(response, ex, _cts.Token);
                        await streamingCall.RequestStream.WriteAsync(errorReq);
                    }
                    if (req != null)
                    {
                        await streamingCall.RequestStream.WriteAsync(req);
                    }
                }
            }, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (streamingCall != null)
            {
                await streamingCall.RequestStream.CompleteAsync();
            }
            if (_cts != null)
            {
                _cts.Cancel();
            }
            if (_readingTask != null)
            {
                await _readingTask;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (streamingCall != null)
            {
                streamingCall.Dispose();
                streamingCall = null;
            }
            if (_cts != null)
            {
                _cts.Dispose();
                _cts = null;
            }
            if (_readingTask != null)
            {
                _readingTask.Dispose();
                _readingTask = null;
            }
            return ValueTask.CompletedTask;
        }
    }
}
