using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using GrpcNotification;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WslNotifydWin.Notifications;
using WslNotifydWin.GrpcServices;

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

    private static (X509Certificate2, X509Certificate2) ReadCertificate()
    {
        using var stdin = Console.OpenStandardInput();
        using var ms = new MemoryStream();
        stdin.CopyTo(ms);
        stdin.Close();
        var data = ms.ToArray();
        var msg = CertificateMessage.Parser.ParseFrom(data);
        var serverCert = new X509Certificate2(msg.ServerCertificate.ToByteArray());
        var clientCert = new X509Certificate2(msg.ClientCertificate.ToByteArray());
        return (serverCert, clientCert);
    }

    private static void Main(string[] args)
    {
        var aumId = "WslNotifyd";
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
        (var serverCert, var clientCert) = ReadCertificate();
        builder.Services
            .AddGrpcClient<Notifier.NotifierClient>(options =>
            {
                options.Address = new Uri(args[0]);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                var logger = serviceProvider.GetRequiredService<ILogger<Program>>(); // TODO: better category
                return new HttpClientHandler()
                {
                    ClientCertificates = {
                        // clientCert should have a private key
                        clientCert,
                    },
                    CheckCertificateRevocationList = false,
                    ClientCertificateOptions = ClientCertificateOption.Manual,
                    ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
                    {
                        if (errors == SslPolicyErrors.None)
                        {
                            return true;
                        }
                        if (cert == null)
                        {
                            return false;
                        }
                        if (cert.Thumbprint != serverCert.Thumbprint)
                        {
                            logger.LogWarning("invalid Thumbprint: {0}", cert.Thumbprint);
                            return false;
                        }
                        chain ??= new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(cert);
                        var result = chain.Build(clientCert);
                        logger.LogDebug("client check: {0}", result);
                        return result;
                    },
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                };
            });

        builder.Services.AddSingleton(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Notification>>();
            var lifetime = serviceProvider.GetRequiredService<IHostApplicationLifetime>();
            return new Notification(aumId, logger, lifetime);
        });
        builder.Services.AddSingleton<IHostedService, ActionInvokedSignalService>();
        builder.Services.AddSingleton<IHostedService, NotificationClosedSignalService>();
        builder.Services.AddSingleton<IHostedService, CloseNotificationMessageService>();
        builder.Services.AddSingleton<IHostedService, NotifyMessageService>();
        builder.Services.AddSingleton<IHostedService, NotificationRepliedSignalService>();
        builder.Services.AddSingleton<IHostedService, ShutdownRequestService>();

        var app = builder.Build();
        app.Run();
    }
}
