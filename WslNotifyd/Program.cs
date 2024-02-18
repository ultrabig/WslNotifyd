using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using WslNotifyd.Extensions;

internal class Program
{
    private static void Main(string[] args)
    {
        var port = "12345";
        var host = "127.0.0.1";
        var uid = "1000";
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddProcessService(new ProcessStartInfo("socat")
        {
            UseShellExecute = false,
            ArgumentList = {
                $"TCP-LISTEN:{port},reuseaddr,bind={host}",
                "UNIX-CLIENT:/run/user/1000/bus",
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
