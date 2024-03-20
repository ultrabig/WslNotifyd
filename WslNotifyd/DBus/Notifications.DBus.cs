using System.Runtime.CompilerServices;
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
        private readonly IHostApplicationLifetime _lifetime;
        private readonly WslNotifydWinProcessService _notifydWinService;
        private uint _sequence = 0;
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        private readonly object _lockOnClose = new object();
        private readonly ManualResetEventSlim _waitOnClose = new ManualResetEventSlim();
        private event Func<Notifications, CloseNotificationEventArgs, Task>? _OnCloseNotification;
        public event Func<Notifications, CloseNotificationEventArgs, Task>? OnCloseNotification
        {
            add
            {
                lock (_lockOnClose)
                {
                    _OnCloseNotification += value;
                    _waitOnClose.Set();
                }
            }
            remove
            {
                lock (_lockOnClose)
                {
                    if (_OnCloseNotification?.GetInvocationList().Length == 1)
                    {
                        _waitOnClose.Reset();
                    }
                    _OnCloseNotification -= value;
                }
            }
        }

        private readonly object _lockOnNotify = new object();
        private readonly ManualResetEventSlim _waitOnNotify = new ManualResetEventSlim();
        private event Func<Notifications, NotifyEventArgs, Task<uint>>? _OnNotify;
        public event Func<Notifications, NotifyEventArgs, Task<uint>>? OnNotify
        {
            add
            {
                lock (_lockOnNotify)
                {
                    _OnNotify += value;
                    _waitOnNotify.Set();
                }
            }
            remove
            {
                lock (_lockOnNotify)
                {
                    if (_OnNotify?.GetInvocationList().Length == 1)
                    {
                        _waitOnNotify.Reset();
                    }
                    _OnNotify -= value;
                }
            }
        }

        private readonly TaskCompletionSource _waitNotificationDuration = new TaskCompletionSource();
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
                if (!_waitNotificationDuration.Task.IsCompleted)
                {
                    _waitNotificationDuration.TrySetResult();
                }
            }
        }

        public Notifications(ILogger<Notifications> logger, IServiceProvider serviceProvider, WslNotifydWinProcessService notifydWinService, IHostApplicationLifetime lifetime)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _notifydWinService = notifydWinService;
            _lifetime = lifetime;
        }

        public void Dispose()
        {
            _waitOnClose.Dispose();
            _waitOnNotify.Dispose();
        }

        public async Task CloseNotificationAsync(uint Id)
        {
            var waitCloseTask = Task.Run(_waitOnClose.Wait);
            await RequestStartAndWaitAsync([waitCloseTask], _lifetime.ApplicationStopping);
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
                notificationId = Interlocked.Increment(ref _sequence);
            }
            else
            {
                notificationId = ReplacesId;
            }

            var waitNotifyTask = Task.Run(_waitOnNotify.Wait);
            await RequestStartAndWaitAsync([waitNotifyTask, _waitNotificationDuration.Task], _lifetime.ApplicationStopping);

            (var doc, var data) = builder.Build(AppName, AppIcon, Summary, Body, Actions, Hints, ExpireTimeout, NotificationDuration);
            var task = _OnNotify?.Invoke(this, new NotifyEventArgs()
            {
                NotificationXml = doc.OuterXml,
                NotificationId = notificationId,
                NotificationData = data,
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

        private async Task RequestStartAndWaitAsync(IEnumerable<Task> waitTasks, CancellationToken cancellationToken = default)
        {
            _notifydWinService.RequestStart();
            var exitTask = _notifydWinService.WaitForExitAsync(cancellationToken);
            var waitTask = Task.WhenAll(waitTasks);
            var result = await Task.WhenAny(exitTask, waitTask);
            if (result == exitTask)
            {
                if (result.IsCanceled)
                {
                    throw new TaskCanceledException(result);
                }
                if (result.IsFaulted)
                {
                    throw result.Exception;
                }
                if (result.IsCompletedSuccessfully)
                {
                    throw new Exception("subprocess exited");
                }
            }
        }

        public class CloseNotificationEventArgs : EventArgs
        {
            public required uint NotificationId { get; init; }
        }

        public class NotifyEventArgs : EventArgs
        {
            public required string NotificationXml { get; init; }
            public required uint NotificationId { get; init; }
            public required IDictionary<string, byte[]> NotificationData { get; init; }
        }
    }
}
