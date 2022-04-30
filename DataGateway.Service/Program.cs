using System;
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

                    string jsonFileNameToAdd =
                        !string.IsNullOrWhiteSpace(env.EnvironmentName)
                         ? $"{RuntimeConfig.CONFIGFILE_NAME}.{env.EnvironmentName}{RuntimeConfig.CONFIG_EXTENSION}"
                         : $"{RuntimeConfig.DefaultRuntimeConfigName}";
                    configuration
                        .AddJsonFile(jsonFileNameToAdd);

                    string? runtimeEnvironmentValue
                        = Environment.GetEnvironmentVariable(RuntimeConfig.RUNTIME_ENVIRONMENT_VAR_NAME);
                    if (runtimeEnvironmentValue != null)
                    {
                        configuration
                            .AddJsonFile($"{RuntimeConfig.CONFIGFILE_NAME}" +
                                $".{runtimeEnvironmentValue}{RuntimeConfig.CONFIG_EXTENSION}");
                    }

                    configuration.AddEnvironmentVariables(prefix: RuntimeConfig.ENVIRONMENT_VAR_PREFIX);
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
