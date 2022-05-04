using System.Collections.Generic;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
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
                .ConfigureAppConfiguration((hostingContext, configurationBuilder) =>
                {
                    configurationBuilder.Sources.Clear();
                    IHostEnvironment env = hostingContext.HostingEnvironment;

                    string configFileName
                        = RuntimeConfigPath.GetFileNameAsPerEnvironment(env.EnvironmentName);
                    Dictionary<string, string> configFileNameMap = new()
                    {
                        {  nameof(RuntimeConfigPath.ConfigFileName),
                             configFileName }
                    };

                    configurationBuilder
                        .AddInMemoryCollection(configFileNameMap);

                    configurationBuilder.AddCommandLine(args);

                    configurationBuilder.AddInMemoryUpdateableConfiguration();
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
