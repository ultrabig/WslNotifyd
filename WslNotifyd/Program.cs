using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using WslNotifyd.DBus;
using WslNotifyd.Extensions;
using WslNotifyd.Libc;

internal class Program
{
    private static string GetSessionBusPath(string uid)
    {
        var busAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        if (busAddress == null)
        {
            throw new Exception("DBUS_SESSION_BUS_ADDRESS is null");
        }
        var entries = AddressEntry.ParseEntries(busAddress);
        if (entries.Length == 0)
        {
            throw new Exception("invalid DBUS_SESSION_BUS_ADDRESS");
        }
        var entry = entries[0];
        var endpoints = entry.ResolveAsync().Result;
        if (endpoints.Length == 0)
        {
            throw new Exception("cannot resolve addresses");
        }
        return endpoints[0];
    }

    private static void Main(string[] args)
    {
        var port = "12345";
        var host = "127.0.0.1";
        var uid = Libc.getuid().ToString();
        var target = GetSessionBusPath(uid);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddProcessService(new ProcessStartInfo("socat")
        {
            UseShellExecute = false,
            ArgumentList = {
                $"TCP-LISTEN:{port},reuseaddr,bind={host}",
                target,
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });
        builder.Services.AddProcessService(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin/scripts/runner.sh"))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin"),
            ArgumentList = {
                $"tcp:port={port},host={host}",
                uid,
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });

        var app = builder.Build();
        app.Run();
    }
}
