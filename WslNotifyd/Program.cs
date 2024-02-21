using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using WslNotifyd.DBus;
using WslNotifyd.Extensions;
using WslNotifyd.Services;

internal class Program
{

    private static void Main(string[] args)
    {
        var listenAddress = "https://127.0.0.1:12345";
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProcessService(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin/scripts/runner.sh"))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "../../../../WslNotifydWin"),
            ArgumentList = {
                listenAddress,
            },
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true,
        });
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IHostedService, DBusNotificationService>();
        builder.Services.AddSingleton(new Notifications());

        var app = builder.Build();
        app.MapGrpcService<NotifierService>();
        app.Run(listenAddress);
    }
}
