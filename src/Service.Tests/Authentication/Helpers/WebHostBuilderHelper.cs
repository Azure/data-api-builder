// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.IdentityModel.Tokens;

namespace Azure.DataApiBuilder.Service.Tests.Authentication.Helpers
{
    /// <summary>
    /// Helps create web host with customized authentication and authorization settings
    /// for usage in unit test classes.
    /// </summary>
    public static class WebHostBuilderHelper
    {
        private const string AUDIENCE = "d727a7e8-1af4-4ce0-8c56-f3107f10bbfd";
        private const string LOCAL_ISSUER = "https://fabrikam.com";

        /// <summary>
        /// Creates customized webhost with:
        /// - DAB's Simulator/ EasyAuth authentication middleware and ClientRoleHeader middleware
        /// - dotnet's authorization middleware.
        /// </summary>
        /// <param name="provider">Runtime configured identity provider name.</param>
        /// <param name="useAuthorizationMiddleware">Whether to include authorization middleware in request pipeline.</param>
        /// <returns>IHost to be used to create a TestServer</returns>
        public static async Task<IHost> CreateWebHost(
            string provider,
            bool useAuthorizationMiddleware)
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider runtimeConfigProvider = new(loader);

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

                            services.AddSingleton(runtimeConfigProvider);

                            // https://github.com/dotnet/aspnetcore/issues/53332#issuecomment-2091861884
                            // AddRouting() adds required services that .NET8 does not add by default
                            // Without this, .NET8 fails to resolve the required services for the middleware
                            // and tests fail.
                            services.AddRouting();

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

        /// <summary>
        /// Creates a webhost with
        /// - dotnet's authentication/authorization middleware configured with
        /// the JwtBearer authentication scheme. Expects a key to be
        /// provided which is used to validate the JWT token.
        /// - DAB's ClientRoleHeader middleware
        /// </summary>
        /// <param name="key">Issuer Signing key for JwtBearerOptions</param>
        /// <returns>IHost to be used to create a TestServer</returns>
        public static async Task<IHost> CreateWebHostCustomIssuer(SecurityKey key)
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider runtimeConfigProvider = new(loader);

            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddAuthentication(defaultScheme: JwtBearerDefaults.AuthenticationScheme)
                                .AddJwtBearer(options =>
                                {
                                    // .NET8 change that required and is compatible with .NET6.
                                    // Required so that legacy URL claim types are used.
                                    // https://github.com/dotnet/aspnetcore/issues/52075#issuecomment-1815584839
                                    options.MapInboundClaims = false;
                                    options.Audience = AUDIENCE;
                                    options.TokenValidationParameters = new()
                                    {
                                        // Valiate the JWT Audience (aud) claim
                                        ValidAudience = AUDIENCE,
                                        ValidateAudience = true,
                                        // Validate the JWT Issuer (iss) claim
                                        ValidIssuer = LOCAL_ISSUER,
                                        ValidateIssuer = true,
                                        // The signing key must match
                                        ValidateIssuerSigningKey = true,
                                        IssuerSigningKey = key,
                                        // Lifetime
                                        ValidateLifetime = true,
                                        // Instructs the asp.net core middleware to use the data in the "roles" claim for User.IsInRole()
                                        // See https://learn.microsoft.com/en-us/dotnet/api/system.security.claims.claimsprincipal.isinrole?view=net-6.0#remarks
                                        RoleClaimType = AuthenticationOptions.ROLE_CLAIM_TYPE
                                    };
                                });
                            services.AddAuthorization();
                            services.AddSingleton(runtimeConfigProvider);
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

                            // app.Run acts as terminating middleware to return 200 if we reach it. Without this,
                            // the Middleware pipeline will return 404 by default.
                            app.Run(async (context) =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                await context.Response.WriteAsync("Successfully validated token!");
                                await context.Response.StartAsync();
                            });
                        });
                })
                .StartAsync();
        }

        /// <summary>
        /// Creates the TestServer with the minimum middleware setup necessary to
        /// test JwtAuthenticationMiddlware
        /// Sends a request with the passed in token to the TestServer created.
        /// </summary>
        /// <param name="key">The JST signing key to setup the TestServer</param>
        /// <param name="token">The JWT value to test against the TestServer</param>
        /// <returns>HttpContext with ClaimsPrincipal to inspect.</returns>
        public static async Task<HttpContext> SendRequestAndGetHttpContextState(
            SecurityKey key,
            string token,
            string? clientRoleHeader = null)
        {
            using IHost host = await CreateWebHostCustomIssuer(key);
            TestServer server = host.GetTestServer();

            return await server.SendAsync(context =>
            {
                if (token is not null)
                {
                    StringValues headerValue = new(new string[] { $"Bearer {token}" });
                    KeyValuePair<string, StringValues> authHeader = new("Authorization", headerValue);
                    context.Request.Headers.Add(authHeader);
                }

                if (clientRoleHeader is not null)
                {
                    KeyValuePair<string, StringValues> easyAuthHeader =
                        new(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRoleHeader);
                    context.Request.Headers.Add(easyAuthHeader);
                }

                context.Request.Scheme = "https";
            });
        }
    }
}
