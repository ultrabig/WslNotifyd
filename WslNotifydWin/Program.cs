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

    private async static Task Main(string[] args)
    {
        using var conn = new Connection(new ClientConnectionOptionsWithUserId(args[0], args[1]));
        var task = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (sender, e) =>
        {
            task.SetResult(true);
        };
        conn.StateChanged += (sender, e) =>
        {
            switch (e.State)
            {
                case ConnectionState.Connected:
                    Console.WriteLine("Connected");
                    break;
                case ConnectionState.Disconnected:
                    Console.WriteLine("Disconnected");
                    task.SetResult(true);
                    break;
            }
        };
        Console.WriteLine("1");
        await conn.ConnectAsync();
        Console.WriteLine("2");
        await conn.RegisterObjectAsync(new Notifications());
        Console.WriteLine("3");
        await conn.RegisterServiceAsync("org.freedesktop.Notifications");
        Console.WriteLine("ctrl-c to stop");
        await task.Task;
    }
}
