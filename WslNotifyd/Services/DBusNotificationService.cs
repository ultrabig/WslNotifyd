using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus;
using WslNotifyd.DBus;

namespace WslNotifyd.Services
{
    class DBusNotificationService(Notifications notif, ILogger<DBusNotificationService> logger, IHostApplicationLifetime lifetime) : IHostedService, IAsyncDisposable
    {

        private Connection? _conn = null;


        private const string serviceName = "org.freedesktop.Notifications";

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _conn = new Connection(Address.Session);
            _conn.StateChanged += HandleStateChanged;
            await _conn.ConnectAsync();
            await _conn.RegisterObjectAsync(notif);
            await _conn.RegisterServiceAsync(serviceName);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_conn != null)
            {
                await _conn.UnregisterServiceAsync(serviceName);
                _conn.UnregisterObject(notif);
                _conn.Dispose();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_conn != null)
            {
                _conn.StateChanged -= HandleStateChanged;
                _conn.Dispose();
                _conn = null;
            }
            return ValueTask.CompletedTask;
        }

        private void HandleStateChanged(object? sender, ConnectionStateChangedEventArgs eventArgs)
        {
            switch (eventArgs.State)
            {
                case ConnectionState.Connected:
                    logger.LogInformation("Connected");
                    break;
                case ConnectionState.Disconnected:
                    logger.LogInformation("Disconnected");
                    lifetime.StopApplication();
                    break;
            }
        }
    }
}
