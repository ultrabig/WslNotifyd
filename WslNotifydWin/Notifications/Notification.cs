using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WslNotifydWin.Notifications
{

    class Notification
    {
        private readonly ILogger<Notification> _logger;
        private readonly ToastNotifier _notifier;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ConcurrentDictionary<string, ToastNotification> _toastHistory = new();
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        public Notification(string aumId, ILogger<Notification> logger, IHostApplicationLifetime lifetime)
        {
            _notifier = ToastNotificationManager.CreateToastNotifier(aumId);
            _logger = logger;
            _lifetime = lifetime;
        }

        public Task CloseNotificationAsync(uint Id)
        {
            _logger.LogInformation("notification {0} has been requested to close", Id);
            if (_toastHistory.TryGetValue(Id.ToString(), out var notif))
            {
                _notifier.Hide(notif);
            }
            return Task.CompletedTask;
        }

        public Task<uint> NotifyAsync(string notificationXml, uint notificationId, IDictionary<string, byte[]> notificationData)
        {
            var doc = new XmlDocument();
            doc.LoadXml(notificationXml);

            var savedNotificationData = new Dictionary<string, Uri>();
            try
            {
                foreach (var (hashString, imageData) in notificationData)
                {
                    var path = Path.GetTempFileName();
                    var uri = new Uri(path);
                    File.WriteAllBytes(path, imageData);
                    savedNotificationData[hashString] = uri;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "error while saving images");
            }

            var imageDeletionDelay = 100;
            try
            {
                var nodesToRemove = new List<IXmlNode>();
                void replaceSrc(string xpath, string attr)
                {
                    foreach (var element in doc.SelectNodes(xpath).Cast<XmlElement>())
                    {
                        var found = false;
                        var src = element.GetAttribute(attr);
                        if (src != null)
                        {
                            foreach (var (hashString, localFileUri) in savedNotificationData)
                            {
                                if (hashString == src)
                                {
                                    element.SetAttribute(attr, localFileUri.ToString());
                                    found = true;
                                    break;
                                }
                            }
                        }
                        if (!found)
                        {
                            nodesToRemove.Add(element);
                        }
                    }
                }
                replaceSrc("//image", "src");
                replaceSrc("//action[@imageUri]", "imageUri");
                foreach (var node in nodesToRemove)
                {
                    node.ParentNode.RemoveChild(node);
                }

                var notif = new ToastNotification(doc)
                {
                    Tag = notificationId.ToString(),
                };

                notif.Activated += HandleActivated;
                notif.Dismissed += HandleDismissed;

                _notifier.Show(notif);

                _toastHistory[notif.Tag] = notif;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "error when showing notification {0}", notificationId);
                imageDeletionDelay = 0;
                throw;
            }
            finally
            {
                var stopTcs = new TaskCompletionSource();
                _lifetime.ApplicationStopping.Register(stopTcs.SetResult);
                Task.WhenAny(Task.Delay(imageDeletionDelay), stopTcs.Task).ContinueWith(_ =>
                {
                    try
                    {
                        foreach (var uri in savedNotificationData.Values)
                        {
                            File.Delete(uri.AbsolutePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "error while deleting images");
                    }
                });
            }
            return Task.FromResult(notificationId);
        }

        private void HandleActivated(ToastNotification sender, object args)
        {
            _logger.LogInformation("notification {0} has been activated", sender.Tag);
            var actionKey = "default";
            const uint reason = 2;
            var id = uint.Parse(sender.Tag);
            if (args is ToastActivatedEventArgs eventArgs)
            {
                if (!string.IsNullOrEmpty(eventArgs.Arguments))
                {
                    actionKey = eventArgs.Arguments;
                }
                if (actionKey == "inline-reply")
                {
                    foreach (var (k, v) in eventArgs.UserInput)
                    {
                        if (k == actionKey && v is string text)
                        {
                            // _logger.LogInformation("UserInput => {0}: {1}", k, v);
                            OnReply?.Invoke((id, text));
                            // FIXME: wait for the notification sender to handle the reply signal 
                            Thread.Sleep(100);
                            break;
                        }
                    }
                }
            }
            OnAction?.Invoke((id, actionKey));
            // FIXME: wait for the notification sender to handle the action signal 
            Thread.Sleep(100);
            OnClose?.Invoke((id, reason));
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
            _logger.LogInformation("notification {0} has been dismissed", sender.Tag);
            OnClose?.Invoke((uint.Parse(sender.Tag), reason));
            _toastHistory.Remove(sender.Tag, out _);
        }
    }
}
