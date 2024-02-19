using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WslNotifyd.Services;

namespace WslNotifyd.Extensions
{
    public static class ProcessServiceExtensions
    {
        public static IServiceCollection AddProcessService(this IServiceCollection services, ProcessStartInfo psi)
        {
            // https://github.com/dotnet/runtime/issues/38751#issuecomment-1158350910
            // <IHostedService> is required for StartAsync to be called
            services.AddSingleton<IHostedService>(serviceProvider =>
            {
                var logger = serviceProvider.GetService<ILogger<ProcessService>>()!;
                var lifetime = serviceProvider.GetService<IHostApplicationLifetime>()!;
                return new ProcessService(logger, psi, lifetime);
            });
            return services;
        }
    }
}
