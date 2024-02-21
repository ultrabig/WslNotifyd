using System.Collections.Concurrent;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WslNotifydWin.Notifications
{

    class Notification
    {
        private readonly ToastNotifier _notifier;
        private readonly ConcurrentDictionary<string, ToastNotification> _toastHistory = new();
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;

        public Notification(string aumId)
        {
            _notifier = ToastNotificationManager.CreateToastNotifier(aumId);
        }

        public Task CloseNotificationAsync(uint Id)
        {
            Console.WriteLine("notification {0} has been requested to close", Id);
            if (_toastHistory.TryGetValue(Id.ToString(), out var notif))
            {
                _notifier.Hide(notif);
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

        private static void AddAudio(IXmlNode targetElement, string? src, bool? loop, bool? silent)
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

        private static string FilterXMLTag(string data)
        {
            try
            {
                var doc = new System.Xml.XmlDocument();
                var root = doc.CreateElement("root");
                root.InnerXml = data;
                return root.InnerText;
            }
            catch (System.Xml.XmlException ex)
            {
                Console.WriteLine("Parse Error. fallback: {0}", ex.ToString());
                return data;
            }
        }

        public Task<uint> NotifyAsync(string AppName, uint ReplacesId, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout, uint NotificationId)
        {
            Console.WriteLine("app_name: {0}, replaces_id: {1}, app_icon: {2}, summary: {3}, body: {4}, actions: [{5}], hints: [{6}], expire_timeout: {7}", AppName, ReplacesId, AppIcon, Summary, Body, string.Join(", ", Actions), string.Join(", ", Hints), ExpireTimeout);
            var content = """<toast><visual><binding template="ToastGeneric"></binding></visual></toast>""";
            var doc = new XmlDocument();
            doc.LoadXml(content);
            var toast = (XmlElement)doc.SelectSingleNode("//toast[1]");

            var binding = (XmlElement)doc.SelectSingleNode("//binding[1]");
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

            uint tagId;
            if (ReplacesId == 0)
            {
                tagId = NotificationId;
            }
            else
            {
                tagId = ReplacesId;
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

            if (ExpireTimeout > 0 && ExpireTimeout <= 5)
            {
                toast.SetAttribute("duration", "short");
            }
            else if (ExpireTimeout == 0 || ExpireTimeout > 5)
            {
                toast.SetAttribute("duration", "long");
            }

            var notif = new ToastNotification(doc)
            {
                // Data = data,
                Tag = tagId.ToString(),
            };

            notif.Activated += HandleActivated;
            notif.Dismissed += HandleDismissed;

            _notifier.Show(notif);

            _toastHistory[notif.Tag] = notif;
            return Task.FromResult(tagId);
        }

        private void HandleActivated(ToastNotification sender, object args)
        {
            Console.WriteLine("notification {0} has been activated", sender.Tag);
            string actionKey = "default";
            const uint reason = 2;
            if (args is ToastActivatedEventArgs eventArgs)
            {
                if (!string.IsNullOrEmpty(eventArgs.Arguments))
                {
                    actionKey = eventArgs.Arguments;
                }
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
