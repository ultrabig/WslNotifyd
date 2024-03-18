using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Tmds.DBus;
using WslNotifyd.NotificationBuilders;
using WslNotifyd.Services;

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

    class Notifications : INotifications, IDisposable
    {
        private readonly ILogger<Notifications> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly WslNotifydWinProcessService _notifydWinService;
        private volatile uint _sequence = 0;
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        private readonly ManualResetEventSlim WaitFirstOnCloseNotification = new ManualResetEventSlim();
        private event Func<Notifications, CloseNotificationEventArgs, Task>? _OnCloseNotification;
        public event Func<Notifications, CloseNotificationEventArgs, Task>? OnCloseNotification
        {
            add
            {
                _OnCloseNotification += value;
                WaitFirstOnCloseNotification.Set();
            }
            remove
            {
                if (_OnCloseNotification?.GetInvocationList().Length == 1)
                {
                    WaitFirstOnCloseNotification.Reset();
                }
                _OnCloseNotification -= value;
            }
        }

        private readonly ManualResetEventSlim WaitFirstOnNotify = new ManualResetEventSlim();
        private event Func<Notifications, NotifyEventArgs, Task<uint>>? _OnNotify;
        public event Func<Notifications, NotifyEventArgs, Task<uint>>? OnNotify
        {
            add
            {
                _OnNotify += value;
                WaitFirstOnNotify.Set();
            }
            remove
            {
                if (_OnNotify?.GetInvocationList().Length == 1)
                {
                    WaitFirstOnNotify.Reset();
                }
                _OnNotify -= value;
            }
        }

        private readonly TaskCompletionSource WaitNotificationDuration = new TaskCompletionSource();
        private const uint defaultDuration = 5;
        private volatile uint _notificationDuration = defaultDuration;
        public uint NotificationDuration
        {
            get => _notificationDuration;
            set
            {
                if (value == 0)
                {
                    _notificationDuration = defaultDuration;
                }
                else
                {
                    _notificationDuration = value;
                }
                if (!WaitNotificationDuration.Task.IsCompleted)
                {
                    WaitNotificationDuration.TrySetResult();
                }
            }
        }

        public Notifications(ILogger<Notifications> logger, IServiceProvider serviceProvider, WslNotifydWinProcessService notifydWinService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _notifydWinService = notifydWinService;
        }

        public void Dispose()
        {
            WaitFirstOnCloseNotification.Dispose();
            WaitFirstOnNotify.Dispose();
        }

        public async Task CloseNotificationAsync(uint Id)
        {
            await _notifydWinService.RequestStart(Id);
            WaitFirstOnCloseNotification.Wait();
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
            return Task.FromResult(("WslNotifyd", "WSL", "0.0.1", "1.2"));
        }

        public async Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            _logger.LogInformation("app_name: {0}, replaces_id: {1}, app_icon: {2}, summary: {3}, body: {4}, actions: [{5}], hints: [{6}], expire_timeout: {7}", AppName, ReplacesId, AppIcon, Summary, Body, string.Join(", ", Actions), string.Join(", ", Hints), ExpireTimeout);

            var builder = new NotificationBuilder(_serviceProvider.GetRequiredService<ILogger<NotificationBuilder>>());
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
            await _notifydWinService.RequestStart(notificationId);

            var firstNotifyTask = Task.Run(WaitFirstOnNotify.Wait);
            await Task.WhenAll(firstNotifyTask, WaitNotificationDuration.Task);

            (var doc, var data) = builder.Build(AppName, AppIcon, Summary, Body, Actions, Hints, ExpireTimeout, NotificationDuration);
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
            _notifydWinService.NotificationHandled(id);
            OnClose?.Invoke((id, reason));
        }

        public void FireOnAction(uint id, string actionKey)
        {
            _notifydWinService.NotificationHandled(id);
            OnAction?.Invoke((id, actionKey));
        }

        public void FireOnReply(uint id, string text)
        {
            _notifydWinService.NotificationHandled(id);
            OnReply?.Invoke((id, text));
        }

        public class CloseNotificationEventArgs : EventArgs
        {
            public required uint NotificationId { get; init; }
        }

        public class NotifyEventArgs : EventArgs
        {
            public required string NotificationXml { get; init; }
            public required uint NotificationId { get; init; }
            public required IDictionary<string, byte[]> NotificionData { get; init; }
        }
    }
}
