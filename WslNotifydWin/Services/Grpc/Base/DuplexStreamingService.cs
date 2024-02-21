using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.Services.Grpc.Base
{
    abstract class DuplexStreamingService<TRequest, TResponse> : IHostedService, IAsyncDisposable
    {
        protected readonly ILogger<DuplexStreamingService<TRequest, TResponse>> logger;
        protected readonly Notifier.NotifierClient client;
        protected readonly Notification notif;
        protected AsyncDuplexStreamingCall<TRequest, TResponse>? streamingCall;
        protected uint serialId = 0;
        private Task? _readingTask;
        private CancellationTokenSource? _cts;

        public DuplexStreamingService(
            ILogger<DuplexStreamingService<TRequest, TResponse>> logger,
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
                    try
                    {
                        var req = await HandleResponseAsync(response, _cts.Token);
                        await streamingCall.RequestStream.WriteAsync(req);
                    }
                    catch (Exception ex)
                    {
                        var req = await HandleErrorAsync(response, ex, _cts.Token);
                        await streamingCall.RequestStream.WriteAsync(req);
                    }
                }
            }, cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
            if (streamingCall != null)
            {
                await streamingCall.RequestStream.CompleteAsync();
            }
            if (_readingTask != null)
            {
                await _readingTask;
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_cts != null)
            {
                _cts.Dispose();
                _cts = null;
            }
            if (streamingCall != null)
            {
                streamingCall.Dispose();
                streamingCall = null;
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
