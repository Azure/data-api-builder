// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryHostDiscoveryTests
    {
        [TestMethod]
        public void CreateHostBuilderNameResolvesToTheOriginalPublicSignature()
        {
            // HostFactoryResolver performs this name-only lookup before checking the
            // signature. Even a nonpublic overload makes discovery ambiguous.
            MethodInfo? factory = typeof(Program).GetMethod(nameof(Program.CreateHostBuilder),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(factory);
            Assert.IsTrue(factory.IsPublic);
            Assert.AreEqual(typeof(IHostBuilder), factory.ReturnType);
            CollectionAssert.AreEqual(
                new[] { typeof(string[]), typeof(bool), typeof(string) },
                Array.ConvertAll(factory.GetParameters(), parameter => parameter.ParameterType));
        }

        [DataTestMethod]
        [DataRow(false, null)]
        [DataRow(true, null)]
        [DataRow(false, "1")]
        [DataRow(true, "1")]
        public async Task WebApplicationFactoryDiscoversTheEntryPointWithoutAmbiguousHostFactories(
            bool useStartupMarker, string? optOut)
        {
            string? previousTestMode = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR);
            string? previousOptOut = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR);
            try
            {
                // Keep collection disabled without reading or changing any destination,
                // SDK setting, or unrelated environment variable.
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR, null);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, optOut);

                if (useStartupMarker)
                {
                    await AssertHostDiscoveryAsync<Startup>();
                }
                else
                {
                    await AssertHostDiscoveryAsync<Program>();
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.OPT_OUT_ENV_VAR, previousOptOut);
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR, previousTestMode);
            }
        }

        private static async Task AssertHostDiscoveryAsync<TEntryPoint>() where TEntryPoint : class
        {
            using HostDiscoveryFactory<TEntryPoint> application = new();
            using HttpClient client = application.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("host-discovered", await response.Content.ReadAsStringAsync());
        }

        private sealed class HostDiscoveryFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
        {
            // Do not override CreateHostBuilder or CreateWebHostBuilder: CreateClient must
            // use production host discovery, which fails before this callback pre-fix.
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                // Replace only startup so the discovered host needs no database, credentials,
                // or telemetry sender, and serves through the factory's in-memory TestServer.
                builder.UseEnvironment("Production")
                    .ConfigureAppConfiguration((_, configuration) => configuration.Sources.Clear())
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .UseStartup<DiscoveryStartup>();
            }
        }

        public sealed class DiscoveryStartup
        {
            public static void Configure(IApplicationBuilder app)
            {
                app.Run(context => context.Response.WriteAsync("host-discovered"));
            }
        }
    }
}
