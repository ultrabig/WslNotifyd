using GrpcNotification;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.UI.ViewManagement;

namespace WslNotifydWin.Services
{
    class MonitorMessageDuration : IHostedService
    {
        private readonly ILogger<MonitorMessageDuration> _logger;
        private readonly Notifier.NotifierClient _client;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly UISettings _settings = new UISettings();

        public MonitorMessageDuration(ILogger<MonitorMessageDuration> logger, Notifier.NotifierClient notifierClient, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _client = notifierClient;
            _lifetime = lifetime;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _settings.MessageDurationChanged += HandleMessageDurationChanged;
            await _client.MessageDurationChangedAsync(new MessageDurationRequest()
            {
                MessageDuration = _settings.MessageDuration,
            }, cancellationToken: cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _settings.MessageDurationChanged -= HandleMessageDurationChanged;
            return Task.CompletedTask;
        }

        private void HandleMessageDurationChanged(UISettings sender, UISettingsMessageDurationChangedEventArgs args)
        {
            _client.MessageDurationChanged(new MessageDurationRequest()
            {
                MessageDuration = _settings.MessageDuration,
            }, cancellationToken: _lifetime.ApplicationStopping);
        }
    }
}
