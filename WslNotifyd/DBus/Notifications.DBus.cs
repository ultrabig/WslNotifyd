using System.Runtime.CompilerServices;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]
namespace WslNotifyd.DBus
{
    [DBusInterface("org.freedesktop.Notifications")]
    interface INotifications : IDBusObject
    {
        Task<string[]> GetCapabilitiesAsync();
        Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout);
        Task CloseNotificationAsync(uint Id);
        Task<(string name, string vendor, string version, string specVersion)> GetServerInformationAsync();
        Task<IDisposable> WatchNotificationClosedAsync(Action<(uint id, uint reason)> handler, Action<Exception>? onError = null);
        Task<IDisposable> WatchActionInvokedAsync(Action<(uint id, string actionKey)> handler, Action<Exception>? onError = null);
    }

    class Notifications : INotifications
    {
        private volatile uint _sequence = 0;
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Func<Notifications, CloseNotificationEventArgs, Task>? OnCloseNotification;
        public event Func<Notifications, NotifyEventArgs, Task<uint>>? OnNotify;

        public async Task CloseNotificationAsync(uint Id)
        {
            var task = OnCloseNotification?.Invoke(this, new CloseNotificationEventArgs()
            {
                NotificationId = Id,
            });
            if (task != null)
            {
                await task;
            }
        }

        public Task<string[]> GetCapabilitiesAsync()
        {
            var capabilities = new string[]
            {
                // "action-icons",
                "actions",
                "body",
                // "body-hyperlinks",
                // "body-images",
                // "body-markup",
                // "icon-multi",
                // "icon-static",
                "persistence",
                "sound",
            };
            return Task.FromResult(capabilities);
        }

        public Task<(string name, string vendor, string version, string specVersion)> GetServerInformationAsync()
        {
            return Task.FromResult(("wsl-notifyd", "WSL", "0.0.1", "1.2"));
        }

        public async Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            uint notificationId;
            if (ReplacesId == 0)
            {
                Interlocked.Increment(ref _sequence);
                notificationId = _sequence;
            }
            else
            {
                notificationId = ReplacesId;
            }
            var task = OnNotify?.Invoke(this, new NotifyEventArgs()
            {
                AppName = AppName,
                ReplacesId = ReplacesId,
                AppIcon = AppIcon,
                Summary = Summary,
                Body = Body,
                Actions = Actions,
                Hints = Hints,
                ExpireTimeout = ExpireTimeout,
                NotificationId = notificationId,
            });
            if (task != null)
            {
                await task;
            }
            return notificationId;
        }

        public Task<IDisposable> WatchNotificationClosedAsync(Action<(uint id, uint reason)> handler, Action<Exception>? onError = null)
        {
            return SignalWatcher.AddAsync(this, nameof(OnClose), handler);
        }

        public Task<IDisposable> WatchActionInvokedAsync(Action<(uint id, string actionKey)> handler, Action<Exception>? onError = null)
        {
            return SignalWatcher.AddAsync(this, nameof(OnAction), handler);
        }

        public void FireOnClose(uint id, uint reason)
        {
            OnClose?.Invoke((id, reason));
        }

        public void FireOnAction(uint id, string actionKey)
        {
            OnAction?.Invoke((id, actionKey));
        }

#nullable disable
        public class CloseNotificationEventArgs : EventArgs
        {
            public uint NotificationId { get; set; }
        }

        public class NotifyEventArgs : EventArgs
        {
            public string AppName { get; set; }
            public uint ReplacesId { get; set; }
            public string AppIcon { get; set; }
            public string Summary { get; set; }
            public string Body { get; set; }
            public string[] Actions { get; set; }
            public IDictionary<string, object> Hints { get; set; }
            public int ExpireTimeout { get; set; }
            public uint NotificationId { get; set; }
        }
    }
#nullable enable
}
