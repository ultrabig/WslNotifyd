using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Tmds.DBus;
using WslNotifyd.NotificationBuilders;

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
        Task<IDisposable> WatchNotificationRepliedAsync(Action<(uint id, string text)> handler, Action<Exception>? onError = null);
    }

    class Notifications : INotifications
    {
        private readonly ILogger<Notifications> _logger;
        private readonly IServiceProvider _serviceProvider;
        private volatile uint _sequence = 0;
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        private readonly TaskCompletionSource WaitFirstOnCloseNotification = new TaskCompletionSource();
        private event Func<Notifications, CloseNotificationEventArgs, Task>? _OnCloseNotification;
        public event Func<Notifications, CloseNotificationEventArgs, Task>? OnCloseNotification
        {
            add
            {
                _OnCloseNotification += value;
                if (!WaitFirstOnCloseNotification.Task.IsCompleted)
                {
                    WaitFirstOnCloseNotification.TrySetResult();
                }
            }
            remove
            {
                _OnCloseNotification -= value;
            }
        }

        private readonly TaskCompletionSource WaitFirstOnNotify = new TaskCompletionSource();
        private event Func<Notifications, NotifyEventArgs, Task<uint>>? _OnNotify;
        public event Func<Notifications, NotifyEventArgs, Task<uint>>? OnNotify
        {
            add
            {
                _OnNotify += value;
                if (!WaitFirstOnNotify.Task.IsCompleted)
                {
                    WaitFirstOnNotify.TrySetResult();
                }
            }
            remove
            {
                _OnNotify -= value;
            }
        }

        public Notifications(ILogger<Notifications> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public async Task CloseNotificationAsync(uint Id)
        {
            await WaitFirstOnCloseNotification.Task;
            var task = _OnCloseNotification?.Invoke(this, new CloseNotificationEventArgs()
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
            var capabilities = new[]
            {
                "action-icons",
                "actions",
                "body",
                // "body-hyperlinks",
                // "body-images",
                // "body-markup",
                // "icon-multi",
                "icon-static",
                "persistence",
                "sound",

                // non-standard
                "inline-reply",
            };
            return Task.FromResult(capabilities);
        }

        public Task<(string name, string vendor, string version, string specVersion)> GetServerInformationAsync()
        {
            return Task.FromResult(("wsl-notifyd", "WSL", "0.0.1", "1.2"));
        }

        public async Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            _logger.LogInformation("app_name: {0}, replaces_id: {1}, app_icon: {2}, summary: {3}, body: {4}, actions: [{5}], hints: [{6}], expire_timeout: {7}", AppName, ReplacesId, AppIcon, Summary, Body, string.Join(", ", Actions), string.Join(", ", Hints), ExpireTimeout);

            var builder = new NotificationBuilder(_serviceProvider.GetRequiredService<ILogger<NotificationBuilder>>());
            (var doc, var data) = builder.Build(AppName, AppIcon, Summary, Body, Actions, Hints, ExpireTimeout);

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

            await WaitFirstOnNotify.Task;
            var task = _OnNotify?.Invoke(this, new NotifyEventArgs()
            {
                NotificationXml = doc.OuterXml,
                NotificationId = notificationId,
                NotificionData = data,
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

        public Task<IDisposable> WatchNotificationRepliedAsync(Action<(uint id, string text)> handler, Action<Exception>? onError = null)
        {
            return SignalWatcher.AddAsync(this, nameof(OnReply), handler);
        }

        public void FireOnClose(uint id, uint reason)
        {
            OnClose?.Invoke((id, reason));
        }

        public void FireOnAction(uint id, string actionKey)
        {
            OnAction?.Invoke((id, actionKey));
        }

        public void FireOnReply(uint id, string text)
        {
            OnReply?.Invoke((id, text));
        }

#nullable disable
        public class CloseNotificationEventArgs : EventArgs
        {
            public uint NotificationId { get; init; }
        }

        public class NotifyEventArgs : EventArgs
        {
            public string NotificationXml { get; init; }
            public uint NotificationId { get; init; }
            public IDictionary<string, byte[]> NotificionData { get; init; }
        }
#nullable enable
    }
}
