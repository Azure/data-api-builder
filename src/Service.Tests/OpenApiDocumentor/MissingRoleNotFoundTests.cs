// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Tests validating that requesting an OpenAPI document for a role not present
    /// in the configuration returns a 404 ProblemDetails response with a descriptive message.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class MissingRoleNotFoundTests
    {
        private const string CONFIG_FILE = "missing-role-notfound-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// Validates that a request for /api/openapi/{nonexistentRole} returns a 404
        /// ProblemDetails response containing the role name in the message extension.
        /// </summary>
        [TestMethod]
        public async Task MissingRole_Returns404ProblemDetailsWithMessage()
        {
            TestHelper.SetupDatabaseEnvironment(DB_ENV);
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            loader.TryLoadKnownConfig(out RuntimeConfig config);

            Entity entity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(null, null, false),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            RuntimeConfig testConfig = config with
            {
                Runtime = config.Runtime with
                {
                    Host = config.Runtime?.Host with { Mode = HostMode.Development }
                },
                Entities = new RuntimeEntities(new Dictionary<string, Entity> { { "book", entity } })
            };

            File.WriteAllText(CONFIG_FILE, testConfig.ToJson());
            string[] args = new[] { $"--ConfigFileName={CONFIG_FILE}" };

            using TestServer server = new(Program.CreateWebHostBuilder(args));
            using HttpClient client = server.CreateClient();

            string missingRole = "nonexistentrole";
            HttpResponseMessage response = await client.GetAsync($"/api/openapi/{missingRole}");

            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, "Expected 404 for a role not in the configuration.");

            string responseBody = await response.Content.ReadAsStringAsync();
            JsonDocument doc = JsonDocument.Parse(responseBody);
            JsonElement root = doc.RootElement;

            Assert.AreEqual("Not Found", root.GetProperty("title").GetString(), "ProblemDetails title should be 'Not Found'.");
            Assert.AreEqual(404, root.GetProperty("status").GetInt32(), "ProblemDetails status should be 404.");
            Assert.IsTrue(root.TryGetProperty("type", out _), "ProblemDetails should contain a 'type' field.");
            Assert.IsTrue(root.TryGetProperty("traceId", out _), "ProblemDetails should contain a 'traceId' field.");

            string message = root.GetProperty("message").GetString();
            Assert.IsTrue(message.Contains(missingRole), $"Message should contain the missing role name '{missingRole}'. Actual: {message}");

            TestHelper.UnsetAllDABEnvironmentVariables();
        }
    }
}
