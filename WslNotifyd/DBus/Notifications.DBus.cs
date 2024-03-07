using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Tmds.DBus;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]
namespace WslNotifyd.DBus
{
    using NotificationImageData = (int width, int height, int rowstride, bool hasAlpha, int bitsPerSample, int channels, byte[] data);

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

        private void AddAction(XmlElement targetElement, string actionId, string action, bool actionIcons, IDictionary<string, byte[]> notificationData)
        {
            var node = targetElement.OwnerDocument.CreateElement("action");
            node.SetAttribute("arguments", actionId);
            var actionIconSet = false;
            if (actionIcons)
            {
                var actionIconData = GetIconData(action, 48);
                if (actionIconData != null)
                {
                    var hashString = GetHashString(actionIconData);
                    notificationData[hashString] = actionIconData;
                    node.SetAttribute("imageUri", hashString);
                    node.SetAttribute("content", "");
                    actionIconSet = true;
                }
            }
            if (!actionIconSet)
            {
                node.SetAttribute("content", action);
            }
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

        private bool CheckAudioSrc(string src)
        {
            // https://learn.microsoft.com/en-us/uwp/schemas/tiles/toastschema/element-audio
            var list = new[]
            {
                "ms-winsoundevent:Notification.Default",
                "ms-winsoundevent:Notification.IM",
                "ms-winsoundevent:Notification.Mail",
                "ms-winsoundevent:Notification.Reminder",
                "ms-winsoundevent:Notification.SMS",
                "ms-winsoundevent:Notification.Looping.Alarm",
                "ms-winsoundevent:Notification.Looping.Alarm2",
                "ms-winsoundevent:Notification.Looping.Alarm3",
                "ms-winsoundevent:Notification.Looping.Alarm4",
                "ms-winsoundevent:Notification.Looping.Alarm5",
                "ms-winsoundevent:Notification.Looping.Alarm6",
                "ms-winsoundevent:Notification.Looping.Alarm7",
                "ms-winsoundevent:Notification.Looping.Alarm8",
                "ms-winsoundevent:Notification.Looping.Alarm9",
                "ms-winsoundevent:Notification.Looping.Alarm10",
                "ms-winsoundevent:Notification.Looping.Call",
                "ms-winsoundevent:Notification.Looping.Call2",
                "ms-winsoundevent:Notification.Looping.Call3",
                "ms-winsoundevent:Notification.Looping.Call4",
                "ms-winsoundevent:Notification.Looping.Call5",
                "ms-winsoundevent:Notification.Looping.Call6",
                "ms-winsoundevent:Notification.Looping.Call7",
                "ms-winsoundevent:Notification.Looping.Call8",
                "ms-winsoundevent:Notification.Looping.Call9",
                "ms-winsoundevent:Notification.Looping.Call10",
            };

            var result = list.Contains(src);
            if (!result)
            {
                _logger.LogWarning("audio src: {0} is not supported", src);
            }
            return result;
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
                _logger.LogInformation(ex, "Parse Error. fallback");
                return data;
            }
        }

        private string GetHashString(byte[] data)
        {
            var hashData = SHA256.HashData(data);
            var b = new StringBuilder();
            foreach (var x in hashData)
            {
                b.Append(x.ToString("x2"));
            }
            return b.ToString();
        }

        private void AddImageData(XmlElement targetElement, byte[] imageData, IDictionary<string, byte[]> notificationData, Dictionary<string, string>? attrs = null)
        {
            var hashString = GetHashString(imageData);
            notificationData[hashString] = imageData;
            AddImage(targetElement, hashString, attrs);
        }

        private byte[]? GetIconData(string iconName, int size)
        {
            // gives assertion error `assertion 'GDK_IS_SCREEN (screen)' failed`
            // using var theme = Gtk.IconTheme.Default;
            using var theme = new Gtk.IconTheme();
            try
            {
                using var icon = theme.LoadIcon(iconName, size, 0);
                if (icon == null)
                {
                    _logger.LogWarning("icon not found: {0}", iconName);
                    return null;
                }
                return icon.SaveToBuffer("png");
            }
            catch (GLib.GException ex)
            {
                _logger.LogWarning(ex, "error while looking up an icon: {0}", iconName);
                return null;
            }
        }

        private byte[]? GetDataFromImagePath(string imagePath)
        {
            if (imagePath.StartsWith("file://") || imagePath.StartsWith('/'))
            {
                string absPath;
                try
                {
                    var uri = new Uri(imagePath);
                    absPath = uri.AbsolutePath;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "uri {0} is malformed", imagePath);
                    return null;
                }
                try
                {
                    using var image = new Gdk.Pixbuf(absPath);
                    return image.SaveToBuffer("png");
                }
                catch (GLib.GException ex)
                {
                    _logger.LogWarning(ex, "error while reading image {0}", imagePath);
                    return null;
                }
            }
            else
            {
                var iconData = GetIconData(imagePath, 256);
                if (iconData == null)
                {
                    _logger.LogWarning("{0} is not valid as file:// uri, absolute path or icon name", imagePath);
                    return null;
                }
                return iconData;
            }
        }

        private byte[]? ToPngData(NotificationImageData data)
        {
            if (data.hasAlpha && data.channels != 4)
            {
                _logger.LogWarning("has_alpha == true and channels != 4");
                return null;
            }
            if (!data.hasAlpha && data.channels != 3)
            {
                _logger.LogWarning("has_alpha == false and channels != 3");
                return null;
            }
            if (data.bitsPerSample != 8)
            {
                _logger.LogWarning("bits_per_sample != 8");
                return null;
            }
            if (data.width * data.channels != data.rowstride)
            {
                _logger.LogWarning("the rowstride is invalid");
                return null;
            }
            if (data.data.Length != data.rowstride * data.height)
            {
                _logger.LogWarning("the data length is invalid");
                return null;
            }
            try
            {
                using var pixbuf = new Gdk.Pixbuf(data.data, Gdk.Colorspace.Rgb, data.hasAlpha, data.bitsPerSample, data.width, data.height, data.rowstride);
                return pixbuf.SaveToBuffer("png");
            }
            catch (GLib.GException ex)
            {
                _logger.LogWarning(ex, "error while loading image data as a gdk-pixbuf");
                return null;
            }
        }

        private bool TryGetHintValue<T>(IDictionary<string, object> hints, string key, out T outValue)
        {
            if (hints.TryGetValue(key, out var valueObj) && valueObj is T v)
            {
                outValue = v;
                return true;
            }
#pragma warning disable CS8601 // Possible null reference assignment.
            outValue = default;
#pragma warning restore CS8601 // Possible null reference assignment.
            return false;
        }

        private bool TryGetHintValue<T>(IDictionary<string, object> hints, IEnumerable<string> keys, out T outValue)
        {
            foreach (var key in keys)
            {
                if (TryGetHintValue(hints, key, out outValue))
                {
                    return true;
                }
            }
#pragma warning disable CS8601 // Possible null reference assignment.
            outValue = default;
#pragma warning restore CS8601 // Possible null reference assignment.
            return false;
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

            if (!TryGetHintValue<bool>(Hints, "action-icons", out var actionIcons))
            {
                actionIcons = false;
            }

            if (Actions.Length > 1)
            {
                var actionsElement = doc.CreateElement("actions");
                for (uint i = 0; i + 1 < Actions.Length; i += 2)
                {
                    AddAction(actionsElement, Actions[i], Actions[i + 1], actionIcons, data);
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
                var appIconData = GetIconData(AppIcon, 96);
                if (appIconData != null)
                {
                    AddImageData(binding, appIconData, data, new() { { "placement", "appLogoOverride" }, });
                }
            }

            var imageAdded = false;
            if (!imageAdded && TryGetHintValue<NotificationImageData>(Hints, ["image-data", "image_data"], out var imageData))
            {
                var pngData = ToPngData(imageData);
                if (pngData != null)
                {
                    AddImageData(binding, pngData, data);
                    imageAdded = true;
                }
            }
            if (!imageAdded && TryGetHintValue<string>(Hints, ["image-path", "image_path"], out var imagePath) && !string.IsNullOrEmpty(imagePath))
            {
                var localImageData = GetDataFromImagePath(imagePath);
                if (localImageData != null)
                {
                    AddImageData(binding, localImageData, data);
                    imageAdded = true;
                }
            }
            if (!imageAdded && TryGetHintValue<NotificationImageData>(Hints, "icon_data", out var iconData))
            {
                var pngData = ToPngData(iconData);
                if (pngData != null)
                {
                    AddImageData(binding, pngData, data);
                    imageAdded = true;
                }
            }

            string? audioSrc = null;
            bool? audioLoop = null;
            bool? audioSuppress = null;
            if (TryGetHintValue<string>(Hints, "sound-name", out var soundName) && CheckAudioSrc(soundName))
            {
                audioSrc = soundName;
            }
            if (TryGetHintValue<bool>(Hints, "suppress-sound", out var suppressSound))
            {
                audioSuppress = suppressSound;
            }
            if (audioSrc != null || audioLoop != null || audioSuppress != null)
            {
                AddAudio(toast, audioSrc, audioLoop, audioSuppress);
            }

            if (TryGetHintValue<byte>(Hints, "urgency", out var urgency) && urgency == 2)
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
#nullable enable
    }
}
