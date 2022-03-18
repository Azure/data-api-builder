using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Azure.DataGateway.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>

            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryUpdateableConfiguration();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });

        // This is used for testing purposes only. The test web server takes in a
        // IWebHostbuilder, instead of a IHostBuilder.
        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((builder) =>
            {
                builder.AddInMemoryUpdateableConfiguration();
            }).UseStartup<Startup>();
    }
}
