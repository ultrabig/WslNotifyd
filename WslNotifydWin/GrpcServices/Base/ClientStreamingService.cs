using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.GrpcServices.Base
{
    abstract class ClientStreamingService<TRequest, TResponse, TService> : IHostedService, IAsyncDisposable
    {
        protected readonly ILogger<TService> logger;
        protected readonly Notifier.NotifierClient client;
        protected readonly Notification notif;
        protected AsyncClientStreamingCall<TRequest, TResponse>? streamingCall;
        private CancellationTokenSource? _cts = null;

        public ClientStreamingService(
            ILogger<TService> logger,
            Notifier.NotifierClient notifierClient,
            Notification notification)
        {
            this.logger = logger;
            client = notifierClient;
            notif = notification;
        }

        protected abstract Task<AsyncClientStreamingCall<TRequest, TResponse>> CreateStreamingCallAsync(CancellationToken cancellationToken);

        protected abstract Task RegisterEventHandlerAsync();

        protected abstract Task UnregisterEventHandlerAsync();

        protected async Task WriteStream(TRequest message)
        {
            if (streamingCall != null)
            {
                await streamingCall.RequestStream.WriteAsync(message);
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            streamingCall = await CreateStreamingCallAsync(_cts.Token);
            await RegisterEventHandlerAsync();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await UnregisterEventHandlerAsync();
            if (streamingCall != null)
            {
                await streamingCall.RequestStream.CompleteAsync();
            }
            if (_cts != null)
            {
                _cts.Cancel();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (streamingCall != null)
            {
                streamingCall.Dispose();
                streamingCall = null;
            }
            return ValueTask.CompletedTask;
        }

    }
}
