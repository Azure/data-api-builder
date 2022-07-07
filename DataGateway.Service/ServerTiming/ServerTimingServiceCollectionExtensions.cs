namespace Azure.DataGateway.Service.ServerTiming
{
    using Microsoft.Extensions.DependencyInjection;

    public static class ServerTimingServiceCollectionExtensions
    {
        public static IServiceCollection AddServerTiming(this IServiceCollection services)
        {
            services.AddScoped<IServerTiming, ServerTiming>();

            return services;
        }
    }
}
