using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WslNotifydWin.Services;

internal class Program
{
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

    private static void Main(string[] args)
    {
        var aumId = "WslNotifyd-aumid";
        SetupRegistry(aumId);

        var initialConfig = new ConfigurationManager();
        initialConfig.AddInMemoryCollection(new Dictionary<string, string?>()
        {
            // if reloadConfigOnChange == true, AddJsonFile freezes on WSL path
            // https://github.com/dotnet/runtime/blob/1381d5ebd2ab1f292848d5b19b80cf71ac332508/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs#L262
            // https://github.com/dotnet/runtime/blob/1381d5ebd2ab1f292848d5b19b80cf71ac332508/src/libraries/Microsoft.Extensions.Hosting/src/HostingHostBuilderExtensions.cs#L241
            ["hostBuilder:reloadConfigOnChange"] = "false",
        });
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings()
        {
            Args = args,
            Configuration = initialConfig,
        });
        builder.Services.AddSingleton<IHostedService>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<DBusNotificationService>>()!;
            var lifetime = serviceProvider.GetService<IHostApplicationLifetime>()!;
            return new DBusNotificationService(args[0], args[1], aumId, logger, lifetime);
        });

        var app = builder.Build();
        app.Run();
    }
}
