// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class CorrelationIdTests
    {
        #region Positive Tests

        /// <summary>
        /// Verify correct passed back correlation id in response if request headers pass in one
        /// </summary>
        [TestMethod(DisplayName = "Response header returns correlation Id if request headers pass in one.")]
        public async Task TestResponseReturnsCorrelationIdFromRequest()
        {
            Guid exptectedCorrelationId = Guid.NewGuid();
            IHost host = await CreateCorrelationIdConfiguredWebHost();
            TestServer server = host.GetTestServer();
            HttpContext returnContext = await server.SendAsync(context =>
            {
                KeyValuePair<string, StringValues> correlationIdHeader = new(HttpHeaders.CORRELATION_ID, exptectedCorrelationId.ToString());
                context.Request.Headers.Add(correlationIdHeader);
            });

            string actualCorrelationId = returnContext.Response.Headers[HttpHeaders.CORRELATION_ID];
            Assert.IsFalse(string.IsNullOrEmpty(actualCorrelationId));
            Assert.AreEqual<string>(expected: exptectedCorrelationId.ToString(), actual: actualCorrelationId);
        }

        /// <summary>
        /// Verify correct generated a correlation id in response if request headers doesn't pass in one
        /// </summary>
        [TestMethod(DisplayName = "Response header returns a generated correlation Id if request headers doesn't have one.")]
        public async Task TestResponseReturnsCorrelationIdIfNonePasses()
        {
            IHost host = await CreateCorrelationIdConfiguredWebHost();
            TestServer server = host.GetTestServer();
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.Remove(HttpHeaders.CORRELATION_ID);
            });

            Assert.IsNotNull(returnContext.Response.Headers[HttpHeaders.CORRELATION_ID]);
            Assert.IsTrue(Guid.TryParse(returnContext.Response.Headers[HttpHeaders.CORRELATION_ID], out _));
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Verify correct generated a correlation id if request headers passed in an invalid one
        /// </summary>
        [TestMethod(DisplayName = "Response header returns a generated correlation Id if request headers passed in invalid guid.")]
        public async Task TestResponseReturnsCorrelationIdIfInvalidGuidPassed()
        {
            IHost host = await CreateCorrelationIdConfiguredWebHost();
            TestServer server = host.GetTestServer();
            HttpContext returnContext = await server.SendAsync(context =>
            {
                KeyValuePair<string, StringValues> correlationIdHeader = new(HttpHeaders.CORRELATION_ID, "Invalid Guid");
                context.Request.Headers.Add(correlationIdHeader);
            });

            Assert.IsNotNull(returnContext.Response.Headers[HttpHeaders.CORRELATION_ID]);
            Assert.IsTrue(Guid.TryParse(returnContext.Response.Headers[HttpHeaders.CORRELATION_ID], out _), message: "Response headers didn't generated a new valid correlation id if user passed one invalid.");
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Spins up a minimal CorrelationId-configured WebHost using the same method as Startup
        /// </summary>
        public static async Task<IHost> CreateCorrelationIdConfiguredWebHost()
        {
            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddHttpContextAccessor();
                        })
                        .Configure(app =>
                        {
                            app.UseMiddleware<CorrelationIdMiddleware>();
                        });
                })
                .StartAsync();
        }

        #endregion
    }
}
