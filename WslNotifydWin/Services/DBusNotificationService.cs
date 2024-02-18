using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tmds.DBus;
using WslNotifydWin.DBus;

namespace WslNotifydWin.Services
{
    public class DBusNotificationService(string address, string userId, string aumId, ILogger<DBusNotificationService> logger, IHostApplicationLifetime lifetime) : IHostedService, IAsyncDisposable
    {

        private Connection? _conn = null;

        private Notifications? _notif = null;

        private const string serviceName = "org.freedesktop.Notifications";

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _conn = new Connection(new ClientConnectionOptionsWithUserId(address, userId));
            _conn.StateChanged += HandleStateChanged;
            _notif = new Notifications(aumId);
            await _conn.ConnectAsync();
            await _conn.RegisterObjectAsync(_notif);
            await _conn.RegisterServiceAsync(serviceName);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_conn != null)
            {
                await _conn.UnregisterServiceAsync(serviceName);
                _conn.UnregisterObject(_notif);
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

        private class ClientConnectionOptionsWithUserId(string address, string userId) : ClientConnectionOptions(address)
        {
            protected override async Task<ClientSetupResult> SetupAsync()
            {
                // https://dbus.freedesktop.org/doc/dbus-specification.html#auth-mechanisms-external
                // Use Linux's uid
                var result = await base.SetupAsync();
                result.UserId = userId;
                return result;
            }
        }
    }
}
