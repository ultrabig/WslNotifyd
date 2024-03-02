using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
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
        private ILogger<Notifications> _logger;
        private volatile uint _sequence = 0;
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;

        private TaskCompletionSource WaitFirstOnCloseNotification = new TaskCompletionSource();
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

        private TaskCompletionSource WaitFirstOnNotify = new TaskCompletionSource();
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

        public Notifications(ILogger<Notifications> logger)
        {
            _logger = logger;
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

        private void AddText(XmlElement targetElement, string text, Dictionary<string, string>? attrs = null)
        {
            var node = targetElement.OwnerDocument.CreateElement("text");
            node.InnerText = text;
            if (attrs != null)
            {
                foreach (var (key, value) in attrs)
                {
                    node.SetAttribute(key, value);
                }
            }
            targetElement.AppendChild(node);
        }

        private void AddAction(XmlElement targetElement, string actionId, string action)
        {
            var node = targetElement.OwnerDocument.CreateElement("action");
            node.SetAttribute("content", action);
            node.SetAttribute("arguments", actionId);
            targetElement.AppendChild(node);
        }

        private void AddAudio(XmlElement targetElement, string? src, bool? loop, bool? silent)
        {
            var node = targetElement.OwnerDocument.CreateElement("audio");
            if (src != null)
            {
                node.SetAttribute("src", src);
            }
            if (loop is bool loopBool)
            {
                node.SetAttribute("loop", loopBool ? "true" : "false");
            }
            if (silent is bool silentBool)
            {
                node.SetAttribute("silent", silentBool ? "true" : "false");
            }
            targetElement.AppendChild(node);
        }

        private void AddImage(XmlElement targetElement, string src, Dictionary<string, string>? attrs = null)
        {
            var node = targetElement.OwnerDocument.CreateElement("image");
            node.SetAttribute("src", src);
            if (attrs != null)
            {
                foreach (var (key, value) in attrs)
                {
                    node.SetAttribute(key, value);
                }
            }
            targetElement.AppendChild(node);
        }

        private string FilterXMLTag(string data)
        {
            try
            {
                var doc = new XmlDocument();
                var root = doc.CreateElement("root");
                root.InnerXml = data;
                return root.InnerText;
            }
            catch (XmlException ex)
            {
                _logger.LogInformation("Parse Error. fallback: {0}", ex.ToString());
                return data;
            }
        }

        public async Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            _logger.LogInformation("app_name: {0}, replaces_id: {1}, app_icon: {2}, summary: {3}, body: {4}, actions: [{5}], hints: [{6}], expire_timeout: {7}", AppName, ReplacesId, AppIcon, Summary, Body, string.Join(", ", Actions), string.Join(", ", Hints), ExpireTimeout);
            var content = """<toast><visual><binding template="ToastGeneric"></binding></visual></toast>""";
            var data = new Dictionary<string, byte[]>();
            var doc = new XmlDocument();
            doc.LoadXml(content);
            var toast = (XmlElement)doc.SelectSingleNode("//toast[1]")!;

            var binding = (XmlElement)doc.SelectSingleNode("//binding[1]")!;
            AddText(binding, Summary);
            AddText(binding, FilterXMLTag(Body));
            AddText(binding, AppName, new() { { "placement", "attribution" }, });

            if (Actions.Length > 1)
            {
                var actionsElement = doc.CreateElement("actions");
                for (uint i = 0; i + 1 < Actions.Length; i += 2)
                {
                    AddAction(actionsElement, Actions[i], Actions[i + 1]);
                }
                toast.AppendChild(actionsElement);
            }

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

            if (!string.IsNullOrEmpty(AppIcon))
            {
                // gives assertion error `assertion 'GDK_IS_SCREEN (screen)' failed`
                // using var theme = Gtk.IconTheme.Default;
                using var theme = new Gtk.IconTheme();
                try
                {
                    using var icon = theme.LoadIcon(AppIcon, 96, 0);
                    if (icon != null)
                    {
                        var buffer = icon.SaveToBuffer("png");
                        var hashData = SHA256.HashData(buffer);
                        var b = new StringBuilder();
                        foreach (var x in hashData)
                        {
                            b.Append(x.ToString("x2"));
                        }
                        var hashString = b.ToString();
                        data[hashString] = buffer;
                        AddImage(binding, hashString, new() { { "placement", "appLogoOverride" }, });
                    }
                }
                catch (GLib.GException ex)
                {
                    _logger.LogWarning("error while looking up an icon: {0}, {1}", AppIcon, ex.ToString());
                }
            }

            string? audioSrc = null;
            bool? audioLoop = null;
            bool? audioSuppress = null;
            if (Hints.TryGetValue("suppress-sound", out var suppressObj) && suppressObj is bool suppressBool)
            {
                audioSuppress = suppressBool;
            }
            if (audioSrc != null || audioLoop != null || audioSuppress != null)
            {
                AddAudio(toast, audioSrc, audioLoop, audioSuppress);
            }

            if (Hints.TryGetValue("urgency", out var urgencyObj) && urgencyObj is byte urgency && urgency == 2)
            {
                toast.SetAttribute("scenario", "urgent");
            }

            // TODO: 5 is configurable on Windows
            if (ExpireTimeout > 0 && ExpireTimeout <= 5)
            {
                toast.SetAttribute("duration", "short");
            }
            else if (ExpireTimeout == 0 || ExpireTimeout > 5)
            {
                toast.SetAttribute("duration", "long");
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
            public string NotificationXml { get; set; }
            public uint NotificationId { get; set; }
            public IDictionary<string, byte[]> NotificionData { get; set; } = new Dictionary<string, byte[]>();
        }
    }
#nullable enable
}
