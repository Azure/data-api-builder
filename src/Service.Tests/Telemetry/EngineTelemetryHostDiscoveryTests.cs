// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryHostDiscoveryTests
    {
        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task WebApplicationFactoryDiscoversTheEntryPointWithoutAmbiguousHostFactories(bool useStartupMarker)
        {
            if (useStartupMarker)
            {
                await AssertHostDiscoveryAsync<Startup>();
            }
            else
            {
                await AssertHostDiscoveryAsync<Program>();
            }
        }

        private static async Task AssertHostDiscoveryAsync<TEntryPoint>() where TEntryPoint : class
        {
            // Preserve the framework's real host discovery. Only replace the application
            // startup so this regression needs no database, credentials or telemetry sender.
            using WebApplicationFactory<TEntryPoint> application = new();
            using WebApplicationFactory<TEntryPoint> configured = application.WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .ConfigureAppConfiguration((_, configuration) => configuration.Sources.Clear())
                .ConfigureLogging(logging => logging.ClearProviders())
                .UseStartup<DiscoveryStartup>());
            using HttpClient client = configured.CreateClient();
            using HttpResponseMessage response = await client.GetAsync("/");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("host-discovered", await response.Content.ReadAsStringAsync());
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
