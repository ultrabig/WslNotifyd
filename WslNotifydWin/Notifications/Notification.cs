using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace WslNotifydWin.Notifications
{

    class Notification : IHostedService, IDisposable
    {
        private readonly string _aumId;
        private readonly ILogger<Notification> _logger;
        private readonly ToastNotifier _notifier;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly object _lockShutdown = new object();
        private CancellationTokenSource? _cancelShutdown;
        private readonly Timer _timer;
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        public Notification(string aumId, ILogger<Notification> logger, IHostApplicationLifetime lifetime)
        {
            _aumId = aumId;
            _notifier = ToastNotificationManager.CreateToastNotifier(aumId);
            _logger = logger;
            _lifetime = lifetime;
            _timer = new Timer(HandleTimer);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // FIXME: remove all existing notifications because they may have a duplicated tag
            ToastNotificationManager.History.Clear(_aumId);
            // TODO: make the period to be configurable
            _timer.Change(new TimeSpan(0, 0, 5), new TimeSpan(0, 0, 30));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Dispose();
            _cancelShutdown?.Dispose();
        }

        public Task CloseNotificationAsync(uint Id)
        {
            if (_notifier.Setting != NotificationSetting.Enabled)
            {
                _logger.LogError($"notification is disabled: {_notifier.Setting}");
                throw new Exception($"notification is disabled: {_notifier.Setting}");
            }

            CancelShutdown();
            _logger.LogInformation("notification {0} has been requested to close", Id);
            try
            {
                if (TryGetNotificationFromHistory(Id.ToString(), out var notif))
                {
                    _notifier.Hide(notif);
                }
            }
            finally
            {
                RegisterShutdown();
            }
            return Task.CompletedTask;
        }

        public Task<uint> NotifyAsync(string notificationXml, uint notificationId, IDictionary<string, byte[]> notificationData)
        {
            if (_notifier.Setting != NotificationSetting.Enabled)
            {
                _logger.LogError($"notification is disabled: {_notifier.Setting}");
                throw new Exception($"notification is disabled: {_notifier.Setting}");
            }

            CancelShutdown();

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
                notif.Failed += HandleFailed;

                _notifier.Show(notif);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "error when showing notification {0}", notificationId);
                imageDeletionDelay = 0;
                throw;
            }
            finally
            {
                RegisterShutdown();
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
                            break;
                        }
                    }
                }
            }
            OnAction?.Invoke((id, actionKey));
            OnClose?.Invoke((id, reason));
            RegisterShutdown();
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
            RegisterShutdown();
        }

        private void HandleFailed(ToastNotification sender, ToastFailedEventArgs args)
        {
            _logger.LogWarning("failed to send notification {0}", sender.Tag);
            RegisterShutdown();
        }

        private void HandleTimer(object? state)
        {
            RegisterShutdown();
        }

        private void RegisterShutdown()
        {
            lock (_lockShutdown)
            {
                _cancelShutdown?.Cancel();
                if (ToastNotificationManager.History.GetHistory(_aumId).Count > 0)
                {
                    return;
                }
                _cancelShutdown?.Dispose();
                _cancelShutdown = new CancellationTokenSource();
                // TODO: make the timeout to be configurable
                Task.Delay(10000, _cancelShutdown.Token).ContinueWith(ta =>
                {
                    if (ta.IsCanceled)
                    {
                        return;
                    }
                    if (ta.IsFaulted)
                    {
                        throw ta.Exception;
                    }
                    _lifetime.StopApplication();
                });
            }
        }

        private void CancelShutdown()
        {
            lock (_lockShutdown)
            {
                _cancelShutdown?.Cancel();
                _cancelShutdown?.Dispose();
                _cancelShutdown = null;
            }
        }

        private bool TryGetNotificationFromHistory(string tag, [MaybeNullWhen(false)] out ToastNotification notif)
        {
            notif = ToastNotificationManager.History.GetHistory(_aumId).FirstOrDefault(n => n.Tag == tag);
            return notif != null;
        }
    }
}
