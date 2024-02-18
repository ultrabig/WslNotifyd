using Microsoft.Win32;
using Tmds.DBus;
using WslNotifydWin.DBus;

internal class Program
{

    private class ClientConnectionOptionsWithUserId : ClientConnectionOptions
    {
        private readonly string _userId;

        public ClientConnectionOptionsWithUserId(string address, string userId) : base(address)
        {
            _userId = userId;
        }

        protected override async Task<ClientSetupResult> SetupAsync()
        {
            // https://dbus.freedesktop.org/doc/dbus-specification.html#auth-mechanisms-external
            // Use Linux's uid
            var result = await base.SetupAsync();
            result.UserId = _userId;
            return result;
        }
    }

    private static void SetupRegistry(string wslAumId)
    {
        // https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast-other-apps#step-1-register-your-app-in-the-registry
        var key = @"Software\Classes\AppUserModelId";
        using var aumSubKey = Registry.CurrentUser.OpenSubKey(key, true);
        if (aumSubKey == null)
        {
            throw new Exception($"Registry {key} not found");
        }
        var aumIdList = aumSubKey.GetSubKeyNames();

        using var wslAumSubKey = aumIdList.Contains(wslAumId) ? aumSubKey.OpenSubKey(wslAumId, true)! : aumSubKey.CreateSubKey(wslAumId);

        var displayValue = wslAumSubKey.GetValue("DisplayName");
        if (displayValue == null)
        {
            wslAumSubKey.SetValue("DisplayName", "WslNotifyd");
        }
    }

    private async static Task Main(string[] args)
    {
        var aumId = "WslNotifyd-aumid";
        SetupRegistry(aumId);
        var exitTask = new TaskCompletionSource();
        // NOTE: CancelKeyPress is actually not called when executed by processes on WSL
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            exitTask.TrySetResult();
        };
        using var conn = new Connection(new ClientConnectionOptionsWithUserId(args[0], args[1]));
        conn.StateChanged += (sender, e) =>
        {
            switch (e.State)
            {
                case ConnectionState.Connected:
                    Console.WriteLine("Connected");
                    break;
                case ConnectionState.Disconnected:
                    Console.WriteLine("Disconnected");
                    exitTask.TrySetResult();
                    break;
            }
        };
        Console.WriteLine("Connecting...");
        await conn.ConnectAsync();
        await conn.RegisterObjectAsync(new Notifications(aumId));
        await conn.RegisterServiceAsync("org.freedesktop.Notifications");
        Console.WriteLine("Ctrl-c to stop");
        await exitTask.Task;
    }
}
