// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Oracle.ManagedDataAccess.Client;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests;

[TestClass, TestCategory(TestCategory.ORACLE)]
[DoNotParallelize]
public class OracleMultiDataSourceIntegrationTests
{
    [DataTestMethod]
    [DataRow("POST")]
    [DataRow("PUT")]
    [DataRow("PATCH")]
    [DataRow("DELETE")]
    public async Task RestMutationsUseEntityNonDefaultOracleDataSource(string method)
    {
        string connectionString = Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING");
        Assert.IsFalse(string.IsNullOrWhiteSpace(connectionString), "Use a dedicated Oracle integration-test schema.");
        string prefix = "DAB_MULTI_" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        string defaultTable = prefix + "_D";
        string linkedTable = prefix + "_L";
        string directory = Path.Combine(Path.GetTempPath(), "dab-oracle-linked-" + Guid.NewGuid().ToString("N"));
        using OracleConnection connection = new(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $"CREATE TABLE {defaultTable} (ID NUMBER PRIMARY KEY, TITLE VARCHAR2(100))");
        bool linkedCreated = false;
        try
        {
            await ExecuteAsync(connection, $"CREATE TABLE {linkedTable} (ID NUMBER PRIMARY KEY, TITLE VARCHAR2(100))");
            linkedCreated = true;
            await ExecuteAsync(connection, $"INSERT INTO {defaultTable} (ID, TITLE) VALUES (17, 'default sentinel')");
            if (method != "POST")
            {
                await ExecuteAsync(connection, $"INSERT INTO {linkedTable} (ID, TITLE) VALUES (17, 'original')");
            }

            Directory.CreateDirectory(directory);
            string rootPath = Path.Combine(directory, "root.json");
            string childPath = Path.Combine(directory, "child.json");
            JsonObject root = JsonNode.Parse("""
                {
                    "data-source": { "database-type": "oracle", "connection-string": "@env('DAB_ORACLE_TEST_CONNECTION_STRING')" },
                    "runtime": {
                        "rest": { "enabled": true }, "graphql": { "enabled": false }, "mcp": { "enabled": true },
                        "host": { "mode": "development" }
                    },
                    "entities": {}
                }
                """)!.AsObject();
            root["entities"]!["RootRecord"] = Entity(defaultTable, readOnly: true);
            root["data-source-files"] = new JsonArray(childPath);
            JsonObject child = JsonNode.Parse("""
                {
                    "data-source": { "database-type": "oracle", "connection-string": "@env('DAB_ORACLE_TEST_CONNECTION_STRING')" },
                    "entities": {}
                }
                """)!.AsObject();
            child["entities"]!["TargetRecord"] = Entity(linkedTable, readOnly: false);
            // Only environment references are written; the test connection string stays in memory.
            await File.WriteAllTextAsync(rootPath, root.ToJsonString());
            await File.WriteAllTextAsync(childPath, child.ToJsonString());

            using IHost host = Program.CreateHostBuilder(new[] { "--ConfigFileName", rootPath, "--no-https-redirect" },
                    runMcpStdio: false, mcpRole: null)
                .UseEnvironment("Development")
                .ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(
                    new Dictionary<string, string> { ["CONNSTRING"] = connectionString }))
                .ConfigureWebHost(web => web.UseTestServer())
                .Build();
            await host.StartAsync();
            using HttpClient client = host.GetTestClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            RuntimeConfigProvider provider = host.Services.GetRequiredService<RuntimeConfigProvider>();
            var config = provider.GetConfig();
            Assert.AreEqual(config.DefaultDataSourceName, config.GetDataSourceNameFromEntityName("RootRecord"));
            Assert.AreNotEqual(config.DefaultDataSourceName, config.GetDataSourceNameFromEntityName("TargetRecord"),
                "The regression requires TargetRecord to belong exclusively to the linked source.");

            string title = "linked " + method;
            string route = method == "POST" ? "/api/TargetRecord" : "/api/TargetRecord/id/17";
            using HttpRequestMessage request = new(new HttpMethod(method), route);
            if (method != "DELETE")
            {
                request.Content = method == "POST" ? JsonContent.Create(new { id = 17, title }) : JsonContent.Create(new { title });
            }

            using HttpResponseMessage response = await client.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(method == "POST" ? HttpStatusCode.Created : method == "DELETE" ? HttpStatusCode.NoContent : HttpStatusCode.OK,
                response.StatusCode, body);
            if (method != "DELETE")
            {
                using JsonDocument result = JsonDocument.Parse(body);
                JsonElement rows = result.RootElement.GetProperty("value");
                Assert.AreEqual(1, rows.GetArrayLength(), "The linked entity's read-policy follow-up must return its mutated row.");
                Assert.AreEqual(17, rows[0].GetProperty("id").GetInt32());
                Assert.AreEqual(title, rows[0].GetProperty("title").GetString());
            }

            if (method == "POST")
            {
                Assert.IsNotNull(response.Headers.Location);
                StringAssert.EndsWith(response.Headers.Location.ToString(), "/id/17");
            }

            using OracleCommand persisted = connection.CreateCommand();
            persisted.CommandText = $"SELECT TITLE FROM {linkedTable} WHERE ID = 17";
            Assert.AreEqual(method == "DELETE" ? null : title, await persisted.ExecuteScalarAsync());
            persisted.CommandText = $"SELECT TITLE FROM {defaultTable} WHERE ID = 17";
            Assert.AreEqual("default sentinel", await persisted.ExecuteScalarAsync(), "The default source's table must remain untouched.");
            await host.StopAsync();
        }
        finally
        {
            if (linkedCreated)
            {
                await ExecuteAsync(connection, $"DROP TABLE {linkedTable} PURGE");
            }

            await ExecuteAsync(connection, $"DROP TABLE {defaultTable} PURGE");
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static JsonObject Entity(string table, bool readOnly)
    {
        JsonObject entity = JsonNode.Parse("""
            {
                "source": "", "rest": true, "graphql": false,
                "mappings": { "ID": "id", "TITLE": "title" },
                "permissions": [{ "role": "anonymous", "actions": [
                    { "action": "read", "policy": { "database": "@item.title ne null" } }
                ] }]
            }
            """)!.AsObject();
        entity["source"] = table;
        if (!readOnly)
        {
            JsonArray actions = entity["permissions"]![0]!["actions"]!.AsArray();
            actions.Add("create");
            actions.Add("update");
            actions.Add("delete");
        }

        return entity;
    }

    private static async Task ExecuteAsync(OracleConnection connection, string sql)
    {
        using OracleCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
