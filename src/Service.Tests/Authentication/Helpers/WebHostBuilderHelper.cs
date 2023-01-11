#nullable enable
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
using Azure.DataApiBuilder.Service.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authentication.Helpers
{
    /// <summary>
    /// Helps create web host with customized authentication and authorization settings
    /// for usage in unit test classes.
    /// </summary>
    public static class WebHostBuilderHelper
    {
        /// <summary>
        /// Creates customized webhost
        /// </summary>
        /// <param name="provider">Runtime configured identity provider name.</param>
        /// <param name="useAuthorizationMiddleware">Whether to include authorization middleware in request pipeline.</param>
        /// <returns>IHost</returns>
        public static async Task<IHost> CreateWebHost(
            string provider,
            bool useAuthorizationMiddleware)
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            Mock<RuntimeConfigPath> runtimeConfigPath = new();
            Mock<RuntimeConfigProvider> runtimeConfigProvider = new(runtimeConfigPath.Object,
                configProviderLogger.Object);

            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            if (string.Equals(provider, SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME, StringComparison.OrdinalIgnoreCase))
                            {
                                services.AddAuthentication(defaultScheme: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME)
                                    .AddSimulatorAuthentication();
                            }
                            else
                            {
                                EasyAuthType easyAuthProvider = (EasyAuthType)Enum.Parse(typeof(EasyAuthType), provider, ignoreCase: true);
                                services.AddAuthentication(defaultScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                                    .AddEasyAuthAuthentication(easyAuthProvider);
                            }

                            services.AddSingleton(runtimeConfigProvider.Object);

                            if (useAuthorizationMiddleware)
                            {
                                services.AddAuthorization();
                            }
                        })
                        .ConfigureLogging(o =>
                        {
                            o.AddFilter(levelFilter => levelFilter <= LogLevel.Information);
                            o.AddDebug();
                            o.AddConsole();
                        })
                        .Configure(app =>
                        {
                            app.UseAuthentication();
                            app.UseClientRoleHeaderAuthenticationMiddleware();

                            if (useAuthorizationMiddleware)
                            {
                                app.UseAuthorization();
                                app.UseClientRoleHeaderAuthorizationMiddleware();
                            }
                            // app.Run acts as terminating middleware to return 200 if we reach it. Without this,
                            // the Middleware pipeline will return 404 by default.
                            app.Run(async (context) =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                await context.Response.WriteAsync("Successful Request");
                                await context.Response.StartAsync();
                            });
                        });
                })
                .StartAsync();
        }
    }
}
