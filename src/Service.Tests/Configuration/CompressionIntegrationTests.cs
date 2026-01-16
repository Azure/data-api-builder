// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DabCompressionLevel = Azure.DataApiBuilder.Config.ObjectModel.CompressionLevel;
using SystemCompressionLevel = System.IO.Compression.CompressionLevel;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    /// <summary>
    /// Integration tests for HTTP response compression middleware.
    /// Validates that compression reduces payload sizes and doesn't break existing functionality.
    /// </summary>
    [TestClass]
    public class CompressionIntegrationTests
    {
        // Sample JSON payload for testing compression
        private static readonly string _sampleJsonPayload = JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(1, 100).Select(i => new
            {
                id = i,
                title = $"Book Title {i}",
                author = $"Author Name {i}",
                description = $"This is a long description for book {i} to ensure we have enough data to compress effectively. " +
                             "Compression works best with repetitive text and structured data like JSON."
            })
        });

        #region Positive Tests

        /// <summary>
        /// Verify that responses are compressed when client sends Accept-Encoding header with gzip.
        /// </summary>
        [TestMethod("Responses are compressed with gzip when Accept-Encoding header is present.")]
        public async Task TestResponseIsCompressedWithGzip()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Optimal);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.AcceptEncoding = "gzip";
            });
            
            // Verify Content-Encoding header is present
            Assert.IsTrue(returnContext.Response.Headers.ContentEncoding.Contains("gzip"), 
                "Response should have gzip Content-Encoding header");
            
            // Verify response body exists by checking if we can read it
            using (var reader = new StreamReader(returnContext.Response.Body))
            {
                string content = await reader.ReadToEndAsync();
                Assert.IsTrue(content.Length > 0, "Response body should not be empty");
            }
        }

        /// <summary>
        /// Verify that responses are compressed with Brotli when client requests it.
        /// </summary>
        [TestMethod("Responses are compressed with Brotli when Accept-Encoding header specifies br.")]
        public async Task TestResponseIsCompressedWithBrotli()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Optimal);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.AcceptEncoding = "br";
            });
            
            Assert.IsTrue(returnContext.Response.Headers.ContentEncoding.Contains("br"), 
                "Response should have br Content-Encoding header");
        }

        /// <summary>
        /// Verify that compression reduces payload size significantly.
        /// </summary>
        [TestMethod("Compression reduces payload size for JSON responses.")]
        public async Task TestCompressionReducesPayloadSize()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Optimal);
            TestServer server = host.GetTestServer();
            
            // Get uncompressed response
            HttpContext uncompressedContext = await server.SendAsync(context =>
            {
                // Don't set Accept-Encoding
            });
            
            using (var ms = new MemoryStream())
            {
                await uncompressedContext.Response.Body.CopyToAsync(ms);
                long uncompressedSize = ms.Length;
                
                // Get compressed response
                HttpContext compressedContext = await server.SendAsync(context =>
                {
                    context.Request.Headers.AcceptEncoding = "gzip";
                });
                
                using (var cms = new MemoryStream())
                {
                    await compressedContext.Response.Body.CopyToAsync(cms);
                    long compressedSize = cms.Length;
                    
                    // Verify compressed size is smaller
                    Assert.IsTrue(compressedSize < uncompressedSize, 
                        $"Compressed size ({compressedSize}) should be less than uncompressed size ({uncompressedSize})");
                    
                    // Calculate compression ratio
                    double compressionRatio = (double)(uncompressedSize - compressedSize) / uncompressedSize * 100;
                    Console.WriteLine($"Compression achieved: {compressionRatio:F2}% reduction (from {uncompressedSize} to {compressedSize} bytes)");
                    
                    // Verify at least some compression occurred (at least 10% for JSON)
                    Assert.IsTrue(compressionRatio > 10, $"Compression ratio should be at least 10%, got {compressionRatio:F2}%");
                }
            }
        }

        /// <summary>
        /// Verify that compression is disabled when level is set to None.
        /// </summary>
        [TestMethod("Responses are not compressed when compression level is None.")]
        public async Task TestCompressionDisabledWhenLevelIsNone()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.None);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.AcceptEncoding = "gzip";
            });
            
            Assert.IsFalse(returnContext.Response.Headers.ContentEncoding.Any(), 
                "Response should not have Content-Encoding header when compression is disabled");
        }

        /// <summary>
        /// Verify that responses are not compressed when client doesn't send Accept-Encoding.
        /// </summary>
        [TestMethod("Responses are not compressed without Accept-Encoding header.")]
        public async Task TestNoCompressionWithoutAcceptEncoding()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Optimal);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                // Don't set Accept-Encoding header
            });
            
            Assert.IsFalse(returnContext.Response.Headers.ContentEncoding.Any(), 
                "Response should not be compressed without Accept-Encoding header");
        }

        /// <summary>
        /// Verify that fastest compression level works correctly.
        /// </summary>
        [TestMethod("Compression works with fastest level.")]
        public async Task TestCompressionWithFastestLevel()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Fastest);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.AcceptEncoding = "gzip";
            });
            
            Assert.IsTrue(returnContext.Response.Headers.ContentEncoding.Contains("gzip"), 
                "Response should be compressed with fastest level");
        }

        /// <summary>
        /// Verify that compressed content can be decompressed correctly.
        /// </summary>
        [TestMethod("Compressed content can be decompressed and is valid JSON.")]
        public async Task TestCompressedContentCanBeDecompressed()
        {
            IHost host = await CreateCompressionConfiguredWebHost(DabCompressionLevel.Optimal);
            TestServer server = host.GetTestServer();
            
            HttpContext returnContext = await server.SendAsync(context =>
            {
                context.Request.Headers.AcceptEncoding = "gzip";
            });
            
            // Read compressed data
            using (var ms = new MemoryStream())
            {
                await returnContext.Response.Body.CopyToAsync(ms);
                byte[] compressedData = ms.ToArray();
                
                // Decompress
                string decompressedContent = await DecompressGzipAsync(compressedData);
                Assert.IsFalse(string.IsNullOrEmpty(decompressedContent), "Decompressed content should not be empty");
                
                // Verify it's valid JSON matching our sample
                JsonDocument doc = JsonDocument.Parse(decompressedContent);
                Assert.IsTrue(doc.RootElement.TryGetProperty("data", out _), "Decompressed JSON should contain 'data' property");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates a minimal compression-configured WebHost for testing.
        /// </summary>
        private static async Task<IHost> CreateCompressionConfiguredWebHost(DabCompressionLevel level)
        {
            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddHttpContextAccessor();
                            
                            // Add response compression based on level
                            if (level != DabCompressionLevel.None)
                            {
                                SystemCompressionLevel systemLevel = level switch
                                {
                                    DabCompressionLevel.Fastest => SystemCompressionLevel.Fastest,
                                    DabCompressionLevel.Optimal => SystemCompressionLevel.Optimal,
                                    _ => SystemCompressionLevel.Optimal
                                };
                                
                                services.AddResponseCompression(options =>
                                {
                                    options.EnableForHttps = true;
                                    options.Providers.Add<GzipCompressionProvider>();
                                    options.Providers.Add<BrotliCompressionProvider>();
                                });
                                
                                services.Configure<GzipCompressionProviderOptions>(options =>
                                {
                                    options.Level = systemLevel;
                                });
                                
                                services.Configure<BrotliCompressionProviderOptions>(options =>
                                {
                                    options.Level = systemLevel;
                                });
                            }
                        })
                        .Configure(app =>
                        {
                            // Add response compression middleware
                            if (level != DabCompressionLevel.None)
                            {
                                app.UseResponseCompression();
                            }
                            
                            // Simple endpoint that returns JSON
                            app.Run(async context =>
                            {
                                context.Response.ContentType = "application/json";
                                await context.Response.WriteAsync(_sampleJsonPayload);
                            });
                        });
                })
                .StartAsync();
        }

        /// <summary>
        /// Decompresses gzip-compressed data.
        /// </summary>
        private static async Task<string> DecompressGzipAsync(byte[] data)
        {
            using (var compressedStream = new MemoryStream(data))
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var resultStream = new MemoryStream())
            {
                await gzipStream.CopyToAsync(resultStream);
                return Encoding.UTF8.GetString(resultStream.ToArray());
            }
        }

        #endregion
    }
}
