using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Tmds.DBus;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

[assembly: InternalsVisibleTo(Tmds.DBus.Connection.DynamicAssemblyName)]
namespace WslNotifydWin.DBus
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
        private uint _sequence = 0;
        private readonly ConcurrentDictionary<string, ToastNotification> _toastHistory = new();
        public ObjectPath ObjectPath => new("/org/freedesktop/Notifications");
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;

        public Task CloseNotificationAsync(uint Id)
        {
            Console.WriteLine("notification {0} has been requested to close", Id);
            var notifier = ToastNotificationManager.CreateToastNotifier();
            if (_toastHistory.TryGetValue(Id.ToString(), out var notif))
            {
                notifier.Hide(notif);
            }
            return Task.CompletedTask;
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

        private static void AddText(IXmlNode targetElement, string text, Dictionary<string, string>? attrs = null)
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

        private static void AddAction(IXmlNode targetElement, string actionId, string action)
        {
            var node = targetElement.OwnerDocument.CreateElement("action");
            node.SetAttribute("content", action);
            node.SetAttribute("arguments", actionId);
            targetElement.AppendChild(node);
        }

        public Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            Console.WriteLine("app_name: {0}, replaces_id: {1}, app_icon: {2}, summary: {3}, body: {4}, actions: {5}, hints: [{6}], expire_timeout: {7}", AppName, ReplacesId, AppIcon, Summary, Body, string.Join(", ", Actions), string.Join(", ", Hints), ExpireTimeout);
            var content = """<toast><visual><binding template="ToastGeneric"></binding></visual></toast>""";
            var doc = new XmlDocument();
            doc.LoadXml(content);
            var toast = doc.SelectSingleNode("//toast[1]");

            var binding = doc.SelectSingleNode("//binding[1]");
            AddText(binding, Summary);
            AddText(binding, Body);
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

            uint tagId;
            if (ReplacesId == 0)
            {
                Interlocked.Increment(ref _sequence);
                tagId = _sequence;
            }
            else
            {
                tagId = ReplacesId;
            }


            var notif = new ToastNotification(doc)
            {
                // Data = data,
                Tag = tagId.ToString(),
            };

            notif.Activated += HandleActivated;
            notif.Dismissed += HandleDismissed;

            // var notifier = ToastNotificationManager.CreateToastNotifier("MicrosoftCorporationII.WindowsSubsystemForLinux_8wekyb3d8bbwe!wsl");
            var notifier = ToastNotificationManager.CreateToastNotifier("WslNotifyd-aumid");
            // var notifier = ToastNotificationManager.CreateToastNotifier();

            notifier.Show(notif);

            _toastHistory[notif.Tag] = notif;
            return Task.FromResult(tagId);
        }

        public Task<IDisposable> WatchNotificationClosedAsync(Action<(uint id, uint reason)> handler, Action<Exception>? onError = null)
        {
            return SignalWatcher.AddAsync(this, nameof(OnClose), handler);
        }

        public Task<IDisposable> WatchActionInvokedAsync(Action<(uint id, string actionKey)> handler, Action<Exception>? onError = null)
        {
            return SignalWatcher.AddAsync(this, nameof(OnAction), handler);
        }

        private void HandleActivated(ToastNotification sender, object args)
        {
            Console.WriteLine("notification {0} has been activated", sender.Tag);
            string actionKey = "";
            const uint reason = 2;
            if (args is ToastActivatedEventArgs eventArgs)
            {
                actionKey = eventArgs.Arguments;
                // foreach (var (k, v) in eventArgs.UserInput)
                // {
                //     Console.WriteLine("{0}: {1}", k, v);
                // }
            }
            OnAction?.Invoke((uint.Parse(sender.Tag), actionKey));
            OnClose?.Invoke((uint.Parse(sender.Tag), reason));
            _toastHistory.Remove(sender.Tag, out _);
        }

        private void HandleDismissed(ToastNotification sender, ToastDismissedEventArgs args)
        {
            uint reason = args.Reason switch
            {
                ToastDismissalReason.TimedOut => 1,
                ToastDismissalReason.UserCanceled => 2,
                ToastDismissalReason.ApplicationHidden => 3,
                _ => 4,
            };
            Console.WriteLine("notification {0} has been dismissed", sender.Tag);
            OnClose?.Invoke((uint.Parse(sender.Tag), reason));
            _toastHistory.Remove(sender.Tag, out _);
        }
    }
}