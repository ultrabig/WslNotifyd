using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
        private readonly IHostApplicationLifetime _lifetime;
        private readonly object _lockShutdown = new object();
        private readonly Timer _timer;
        private readonly ConcurrentDictionary<string, ToastNotification> _notifications = [];
        private CancellationTokenSource? _cancelShutdown;
        private ToastNotifier Notifier => ToastNotificationManager.CreateToastNotifier(_aumId);
        private ToastNotificationHistory History => ToastNotificationManager.History;
        public event Action<(uint id, string actionKey)>? OnAction;
        public event Action<(uint id, uint reason)>? OnClose;
        public event Action<(uint id, string text)>? OnReply;

        public Notification(string aumId, ILogger<Notification> logger, IHostApplicationLifetime lifetime)
        {
            _aumId = aumId;
            _logger = logger;
            _lifetime = lifetime;
            _timer = new Timer(HandleTimer);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // FIXME: remove all existing notifications because they may have a duplicated tag
            History.Clear(_aumId);
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
            ThrowIfNotificationIsDisabled();

            CancelShutdown();
            _logger.LogInformation("notification {0} has been requested to close", Id);
            try
            {
                if (_notifications.TryRemove(Id.ToString(), out var notif))
                {
                    Notifier.Hide(notif);
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
            ThrowIfNotificationIsDisabled();

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

                _notifications[notif.Tag] = notif;
                Notifier.Show(notif);
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
                var reg = _lifetime.ApplicationStopping.Register(stopTcs.SetResult);
                Task.WhenAny(Task.Delay(imageDeletionDelay), stopTcs.Task).ContinueWith(_ =>
                {
                    reg.Dispose();
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
            _notifications.TryRemove(sender.Tag, out _);
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
            // Don't remove from _notification because it may stay in the Action Center
            uint reason = args.Reason switch
            {
                ToastDismissalReason.TimedOut => 1,
                ToastDismissalReason.UserCanceled => 2,
                ToastDismissalReason.ApplicationHidden => 3,
                _ => 4,
            };
            _logger.LogInformation("notification {0} has been dismissed, reason: {1}", sender.Tag, args.Reason);
            OnClose?.Invoke((uint.Parse(sender.Tag), reason));
            RegisterShutdown();
        }

        private void HandleFailed(ToastNotification sender, ToastFailedEventArgs args)
        {
            _logger.LogWarning(args.ErrorCode, "failed to send notification {0}", sender.Tag);
            _notifications.TryRemove(sender.Tag, out _);
            RegisterShutdown();
        }

        private void HandleTimer(object? state)
        {
            RegisterShutdown();
        }

        private void ThrowIfNotificationIsDisabled()
        {
            NotificationSetting setting;
            try
            {
                setting = Notifier.Setting;
            }
            catch (Exception ex)
            {
                const int E_ELEMENT_NOT_FOUND = unchecked((int)0x80070490);
                if (ex is COMException comEx && comEx.HResult == E_ELEMENT_NOT_FOUND)
                {
                    // Accessing the setting before sending the first notification will throw E_ELEMENT_NOT_FOUND
                    _logger.LogInformation(comEx, "Cannot get notification setting, maybe it is the first run");
                }
                else
                {
                    _logger.LogWarning(ex, "Error while getting notification setting, continue anyway");
                }
                return;
            }
            if (setting != NotificationSetting.Enabled)
            {
                _logger.LogError($"notification is disabled: {setting}");
                throw new Exception($"notification is disabled: {setting}");
            }
        }

        private void RegisterShutdown()
        {
            lock (_lockShutdown)
            {
                var history = GetHistory();
                var tags = history.Select(n => n.Tag).ToArray();
                _logger.LogDebug("current history: {0}", string.Join(",", tags));
                // remove notifications that have been already dismissed
                foreach (var dismissed in _notifications.Keys.Except(tags))
                {
                    _notifications.TryRemove(dismissed, out _);
                }
                _cancelShutdown?.Cancel();
                if (history.Count > 0)
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

        private IReadOnlyList<ToastNotification> GetHistory() => History.GetHistory(_aumId);
    }
}
