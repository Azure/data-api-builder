using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.IO;

namespace Azure.DataGateway.Service.Functions
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IHost host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureAppConfiguration(config =>
                    config
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddEnvironmentVariables()
                )
                .ConfigureServices((context, services) =>
                {
                    Startup.DoConfigureServices(services, context.Configuration);
                })
                .Build();

            host.Run();
        }
    }
}
