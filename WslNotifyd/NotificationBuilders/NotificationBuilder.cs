using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace WslNotifyd.NotificationBuilders
{
    using NotificationImageData = (int width, int height, int rowstride, bool hasAlpha, int bitsPerSample, int channels, byte[] data);

    class NotificationBuilder
    {
        readonly ILogger<NotificationBuilder> _logger;
        readonly XmlDocument _doc = new XmlDocument();
        readonly IDictionary<string, byte[]> _data = new Dictionary<string, byte[]>();

        public NotificationBuilder(ILogger<NotificationBuilder> logger)
        {
            _logger = logger;
        }

        private void AddText(XmlElement targetElement, string text, Dictionary<string, string>? attrs = null)
        {
            var el = targetElement.OwnerDocument.CreateElement("text");
            el.InnerText = text;
            if (attrs != null)
            {
                foreach (var (key, value) in attrs)
                {
                    el.SetAttribute(key, value);
                }
            }
            targetElement.AppendChild(el);
        }

        private XmlElement CreateAction(XmlElement targetElement, string actionId, string action, bool actionIcons, Dictionary<string, string>? attrs = null)
        {
            var el = targetElement.OwnerDocument.CreateElement("action");
            el.SetAttribute("arguments", actionId);
            var content = action;
            if (actionIcons)
            {
                var actionIconData = GetIconData(action, 48);
                if (actionIconData != null)
                {
                    var hashString = GetHashString(actionIconData);
                    _data[hashString] = actionIconData;
                    el.SetAttribute("imageUri", hashString);
                    content = "";
                }
            }
            el.SetAttribute("content", content);
            if (attrs != null)
            {
                foreach (var (key, value) in attrs)
                {
                    el.SetAttribute(key, value);
                }
            }
            return el;
        }

        private XmlElement CreateInput(XmlElement targetElement, string id, string type, string placeHolderContent)
        {
            var el = targetElement.OwnerDocument.CreateElement("input");
            el.SetAttribute("id", id);
            el.SetAttribute("type", type);
            el.SetAttribute("placeHolderContent", placeHolderContent);
            return el;
        }

        private void AddAudio(XmlElement targetElement, string? src, bool? loop, bool? silent)
        {
            var el = targetElement.OwnerDocument.CreateElement("audio");
            if (src != null)
            {
                el.SetAttribute("src", src);
            }
            if (loop is bool loopBool)
            {
                el.SetAttribute("loop", loopBool ? "true" : "false");
            }
            if (silent is bool silentBool)
            {
                el.SetAttribute("silent", silentBool ? "true" : "false");
            }
            targetElement.AppendChild(el);
        }

        private void AddImage(XmlElement targetElement, string src, Dictionary<string, string>? attrs = null)
        {
            var el = targetElement.OwnerDocument.CreateElement("image");
            el.SetAttribute("src", src);
            if (attrs != null)
            {
                foreach (var (key, value) in attrs)
                {
                    el.SetAttribute(key, value);
                }
            }
            targetElement.AppendChild(el);
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

        private void AddImageData(XmlElement targetElement, byte[] imageData, Dictionary<string, string>? attrs = null)
        {
            var hashString = GetHashString(imageData);
            _data[hashString] = imageData;
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

        private byte[]? GetDataFromImagePath(string imagePath, int size)
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
                var iconData = GetIconData(imagePath, size);
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

        private void Clear()
        {
            var content = """<toast><visual><binding template="ToastGeneric"></binding></visual></toast>""";
            _doc.LoadXml(content);
            _data.Clear();
        }

        public (XmlDocument, IDictionary<string, byte[]>) Build(string AppName, string AppIcon, string Summary, string Body, string[] Actions, IDictionary<string, object> Hints, int ExpireTimeout)
        {
            Clear();
            var toast = (XmlElement)_doc.SelectSingleNode("//toast[1]")!;

            var binding = (XmlElement)_doc.SelectSingleNode("//binding[1]")!;
            AddText(binding, Summary);
            AddText(binding, FilterXMLTag(Body));
            AddText(binding, AppName, new() { { "placement", "attribution" }, });

            if (!TryGetHintValue<bool>(Hints, "action-icons", out var actionIcons))
            {
                actionIcons = false;
            }

            var inlineReplyAdded = false;
            if (Actions.Length > 1)
            {
                var actionsElement = _doc.CreateElement("actions");
                var inputs = new List<XmlElement>();
                var actions = new List<XmlElement>();
                for (uint i = 0; i + 1 < Actions.Length; i += 2)
                {
                    var actionId = Actions[i];
                    var actionText = Actions[i + 1];
                    var attrs = new Dictionary<string, string>();

                    if (actionId == "inline-reply")
                    {
                        if (inlineReplyAdded)
                        {
                            _logger.LogWarning("duplicated inline-reply");
                            continue;
                        }
                        inlineReplyAdded = true;
                        if (!TryGetHintValue<string>(Hints, "x-kde-reply-placeholder-text", out var placeholder))
                        {
                            placeholder = "";
                        }
                        var id = actionId;
                        var input = CreateInput(actionsElement, id, "text", placeholder);
                        inputs.Add(input);
                        attrs["hint-inputId"] = id;
                    }

                    if (actionId == "default")
                    {
                        _logger.LogInformation("default key found, not adding a button");
                        continue;
                    }

                    if (actionId == "settings")
                    {
                        attrs["placement"] = "contextMenu";
                    }

                    var action = CreateAction(actionsElement, actionId, actionText, actionIcons, attrs);
                    actions.Add(action);
                }
                foreach (var el in inputs.Concat(actions))
                {
                    actionsElement.AppendChild(el);
                }
                toast.AppendChild(actionsElement);
            }

            if (!string.IsNullOrEmpty(AppIcon))
            {
                var appIconData = GetDataFromImagePath(AppIcon, 96);
                if (appIconData != null)
                {
                    AddImageData(binding, appIconData, new() { { "placement", "appLogoOverride" }, });
                }
            }

            var imageAdded = false;
            if (TryGetHintValue<NotificationImageData>(Hints, ["image-data", "image_data"], out var imageData))
            {
                var pngData = ToPngData(imageData);
                if (pngData != null)
                {
                    AddImageData(binding, pngData);
                    imageAdded = true;
                }
            }
            if (!imageAdded && TryGetHintValue<string>(Hints, ["image-path", "image_path"], out var imagePath) && !string.IsNullOrEmpty(imagePath))
            {
                var localImageData = GetDataFromImagePath(imagePath, 256);
                if (localImageData != null)
                {
                    AddImageData(binding, localImageData);
                    imageAdded = true;
                }
            }
            if (!imageAdded && TryGetHintValue<NotificationImageData>(Hints, "icon_data", out var iconData))
            {
                var pngData = ToPngData(iconData);
                if (pngData != null)
                {
                    AddImageData(binding, pngData);
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

            return (_doc, _data);
        }
    }
}
