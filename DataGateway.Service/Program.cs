using System;
using System.IO;
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
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    configuration.Sources.Clear();
                    IHostEnvironment env = hostingContext.HostingEnvironment;

                    string configFileNameKey
                        = RuntimeConfigPath.GetFileNameAsPerEnvironment(env.EnvironmentName);
                    string path = Path.Combine(
                        Directory.GetCurrentDirectory(), configFileNameKey);
                    configuration
                        .AddKeyPerFile(directoryPath: path);

                    configuration.AddEnvironmentVariables(prefix: RuntimeConfigPath.ENVIRONMENT_VAR_PREFIX);
                    configuration.AddCommandLine(args);
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
