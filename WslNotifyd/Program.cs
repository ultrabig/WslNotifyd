using System.Diagnostics;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Google.Protobuf;
using GrpcNotification;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using WslNotifyd.DBus;
using WslNotifyd.Extensions;
using WslNotifyd.Services;

internal class Program
{

    private static (X509Certificate2, X509Certificate2) CreateCerts()
    {
        // if (File.Exists("./server.pfx") && File.Exists("./client.pfx"))
        // {
        //     Console.WriteLine("reusing certs");
        //     return (new X509Certificate2("./server.pfx"), new X509Certificate2("./client.pfx"));
        // }
        var now = DateTimeOffset.UtcNow;

        using var rootRsa = RSA.Create(4096);
        using var rsa = RSA.Create(4096);

        var rootReq = new CertificateRequest("CN=WslNotifyd", rootRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        // rootReq.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
        rootReq.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootReq.PublicKey, false));
        var serverCert = rootReq.CreateSelfSigned(now.AddDays(-2), now.AddDays(365));

        var req = new CertificateRequest("CN=WslNotifydWin", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, false));
        // req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, true));
        // req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.8") }, true));
        req.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(req.PublicKey, false));
        using var clientCertPublic = req.Create(serverCert, now.AddDays(-2), now.AddDays(365), new byte[] { 1, 2, 3, 4 });
        var clientCert = clientCertPublic.CopyWithPrivateKey(rsa);

        // File.WriteAllBytes("./server.pfx", serverCert.Export(X509ContentType.Pfx));
        // File.WriteAllBytes("./client.pfx", clientCert.Export(X509ContentType.Pfx));
        return (serverCert, clientCert);
    }

    private static void Main(string[] args)
    {
        var listenAddress = "https://127.0.0.1:12345";
        (var serverCert, var clientCert) = CreateCerts();
        Console.WriteLine("Server Cert: {0}, Client Cert: {1}", serverCert.Thumbprint, clientCert.Thumbprint);
        Console.WriteLine("Server Serial: {0}, Client Serial: {1}", serverCert.SerialNumber, clientCert.SerialNumber);
        var builder = WebApplication.CreateBuilder(args);
        // builder.Logging.SetMinimumLevel(LogLevel.Trace);

        var msg = new CertificateMessage()
        {
            ServerCertificate = ByteString.CopyFrom(serverCert.Export(X509ContentType.Cert)),
            ClientCertificate = ByteString.CopyFrom(clientCert.Export(X509ContentType.Pfx)),
        };
        clientCert.Dispose();
        clientCert = null;
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
        }, msg.ToByteArray());
        msg = null;
        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IHostedService, DBusNotificationService>();
        builder.Services.AddSingleton(new Notifications());
        builder.Services.Configure<KestrelServerOptions>(kestrelOptions =>
        {
            kestrelOptions.ConfigureHttpsDefaults(httpsOptions =>
            {
                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                httpsOptions.ClientCertificateValidation = (cert, chain, errors) =>
                {
                    if (errors == SslPolicyErrors.None)
                    {
                        return true;
                    }
                    Console.WriteLine("errors: {0}", errors);
                    Console.WriteLine("client presented: {0}", cert.Thumbprint);
                    chain ??= new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(serverCert);
                    var result = chain.Build(cert);
                    Console.WriteLine("server check: {0}", result);
                    return result;
                };
                httpsOptions.ServerCertificate = serverCert;
                httpsOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
            });
        });

        var app = builder.Build();
        app.MapGrpcService<NotifierService>();
        app.Run(listenAddress);
    }
}
