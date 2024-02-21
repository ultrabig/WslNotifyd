using Grpc.Core;
using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifydWin.Notifications;

namespace WslNotifydWin.Services.Grpc.Base
{
    abstract class ClientStreamingService<TRequest, TResponse> : IHostedService, IAsyncDisposable
    {
        protected readonly ILogger<ClientStreamingService<TRequest, TResponse>> logger;
        protected readonly Notifier.NotifierClient client;
        protected readonly Notification notif;
        protected AsyncClientStreamingCall<TRequest, TResponse>? streamingCall;

        public ClientStreamingService(
            ILogger<ClientStreamingService<TRequest, TResponse>> logger,
            Notifier.NotifierClient notifierClient,
            Notification notification)
        {
            this.logger = logger;
            client = notifierClient;
            notif = notification;
        }

        protected abstract Task<AsyncClientStreamingCall<TRequest, TResponse>> CreateStreamingCallAsync(CancellationToken cancellationToken);

        protected abstract Task RegisterEventHandlerAsync(CancellationToken cancellationToken);

        protected abstract Task UnregisterEventHandlerAsync(CancellationToken cancellationToken);

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            streamingCall = await CreateStreamingCallAsync(cancellationToken);
            await RegisterEventHandlerAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await UnregisterEventHandlerAsync(cancellationToken);
            if (streamingCall != null)
            {
                await streamingCall.RequestStream.CompleteAsync();
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
