using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataGateway.Service.ServerTiming
{
    public static class ServerTimingServiceCollectionExtensions
    {
        public static IServiceCollection AddServerTiming(this IServiceCollection services)
        {
            services.AddScoped<IServerTiming, ServerTiming>();

            return services;
        }
    }
}
