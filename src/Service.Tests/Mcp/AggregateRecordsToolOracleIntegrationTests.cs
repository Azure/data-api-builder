// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp;

[TestClass, TestCategory(TestCategory.ORACLE)]
[DoNotParallelize]
public class AggregateRecordsToolOracleIntegrationTests : McpToolTestBase
{
    [ClassInitialize]
    public static async Task SetupAsync(TestContext context)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DAB_ORACLE_TEST_CONNECTION_STRING")),
            "Set DAB_ORACLE_TEST_CONNECTION_STRING to a dedicated Oracle test schema.");
        DatabaseEngine = TestCategory.ORACLE;
        _queryEngineFactory = new Mock<IQueryEngineFactory>();
        await InitializeTestFixture();
    }

    [TestInitialize]
    public async Task ResetDatabaseAsync() => await ResetDbStateAsync();

    [ClassCleanup]
    public static void CleanupFixture()
    {
        _application?.Dispose();
        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    [DataTestMethod]
    [DataRow("count", 3)]
    [DataRow("sum", 6)]
    [DataRow("avg", 2)]
    [DataRow("min", 1)]
    [DataRow("max", 3)]
    public async Task NumericAggregatesReturnExactValues(string function, int expected)
    {
        JsonElement rows = await SuccessAsync(new() { ["function"] = function, ["field"] = "id" });
        Assert.AreEqual(1, rows.GetArrayLength());
        Assert.AreEqual((decimal)expected, rows[0].GetProperty($"{function}_id").GetDecimal());
    }

    [DataTestMethod]
    [DataRow(false, 3)]
    [DataRow(true, 2)]
    public async Task CountSupportsDistinctValues(bool distinct, int expected)
    {
        JsonElement rows = await SuccessAsync(new() { ["function"] = "count", ["field"] = "publisher_id", ["distinct"] = distinct });
        Assert.AreEqual(expected, rows[0].GetProperty("count_publisher_id").GetInt32());
    }

    [TestMethod]
    public async Task CountStarAndFilterReturnExactCounts()
    {
        JsonElement all = await SuccessAsync(new() { ["function"] = "count" });
        Assert.AreEqual(3, all[0].GetProperty("count").GetInt32());
        JsonElement filtered = await SuccessAsync(new() { ["function"] = "count", ["filter"] = "publisher_id eq 1234" });
        Assert.AreEqual(2, filtered[0].GetProperty("count").GetInt32());
    }

    [DataTestMethod]
    [DataRow("count")]
    [DataRow("sum")]
    [DataRow("avg")]
    [DataRow("min")]
    [DataRow("max")]
    public async Task EmptyInputPreservesAggregateSemantics(string function)
    {
        JsonElement rows = await SuccessAsync(new() { ["function"] = function, ["field"] = "id", ["filter"] = "id lt 0" });
        Assert.AreEqual(1, rows.GetArrayLength());
        JsonElement value = rows[0].GetProperty($"{function}_id");
        if (function == "count")
        {
            Assert.AreEqual(0, value.GetInt32());
        }
        else
        {
            Assert.AreEqual(JsonValueKind.Null, value.ValueKind);
        }
    }

    [TestMethod]
    public async Task NullValuesAreExcludedFromColumnAggregates()
    {
        await ExecuteSqlAsync("ALTER TABLE BOOKS MODIFY (PUBLISHER_ID NULL)");
        await ExecuteSqlAsync("UPDATE BOOKS SET PUBLISHER_ID = NULL WHERE ID = 3");
        JsonElement all = await SuccessAsync(new() { ["function"] = "count" });
        JsonElement column = await SuccessAsync(new() { ["function"] = "count", ["field"] = "publisher_id" });
        JsonElement average = await SuccessAsync(new() { ["function"] = "avg", ["field"] = "publisher_id" });
        Assert.AreEqual(3, all[0].GetProperty("count").GetInt32());
        Assert.AreEqual(2, column[0].GetProperty("count_publisher_id").GetInt32());
        Assert.AreEqual(1234m, average[0].GetProperty("avg_publisher_id").GetDecimal());
    }

    [DataTestMethod]
    [DataRow("asc", 2345, 1234)]
    [DataRow("desc", 1234, 2345)]
    public async Task GroupsAreOrderedByAggregateValue(string direction, int firstPublisher, int secondPublisher)
    {
        JsonElement rows = await SuccessAsync(new()
        {
            ["function"] = "count",
            ["groupby"] = new[] { "publisher_id" },
            ["orderby"] = direction
        });
        CollectionAssert.AreEqual(new[] { firstPublisher, secondPublisher }, rows.EnumerateArray().Select(r => r.GetProperty("publisher_id").GetInt32()).ToArray());
        Assert.AreEqual(firstPublisher == 1234 ? 2 : 1, rows[0].GetProperty("count").GetInt32());
    }

    [TestMethod]
    public async Task HavingFiltersGroupsUsingBoundValues()
    {
        JsonElement rows = await SuccessAsync(new()
        {
            ["function"] = "count",
            ["groupby"] = new[] { "publisher_id" },
            ["having"] = new { gt = 1, @in = new[] { 2, 3 } }
        });
        Assert.AreEqual(1, rows.GetArrayLength());
        Assert.AreEqual(1234, rows[0].GetProperty("publisher_id").GetInt32());
        Assert.AreEqual(2, rows[0].GetProperty("count").GetInt32());
    }

    [DataTestMethod]
    [DataRow("asc")]
    [DataRow("desc")]
    public async Task PaginationBreaksAggregateTiesAndReturnsEveryGroup(string direction)
    {
        // Every group has count=1. Grouping columns must break the aggregate ties.
        List<int> seen = new();
        string cursor = null;
        for (int page = 0; page < 3; page++)
        {
            Dictionary<string, object?> args = new()
            {
                ["function"] = "count",
                ["groupby"] = new[] { "id" },
                ["orderby"] = direction,
                ["first"] = 1
            };
            if (cursor is not null)
            {
                args["after"] = cursor;
            }

            JsonElement result = await SuccessAsync(args);
            JsonElement items = result.GetProperty("items");
            Assert.AreEqual(1, items.GetArrayLength());
            seen.Add(items[0].GetProperty("id").GetInt32());
            Assert.AreEqual(1, items[0].GetProperty("count").GetInt32());
            Assert.AreEqual(page < 2, result.GetProperty("hasNextPage").GetBoolean());
            cursor = result.GetProperty("endCursor").GetString();
            Assert.IsFalse(string.IsNullOrEmpty(cursor));
        }

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, seen);
    }

    [TestMethod]
    public async Task EmptyGroupedPageReturnsNoRowsOrCursor()
    {
        JsonElement result = await SuccessAsync(new()
        {
            ["function"] = "sum",
            ["field"] = "id",
            ["groupby"] = new[] { "publisher_id" },
            ["first"] = 2,
            ["after"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("10"))
        });
        Assert.AreEqual(0, result.GetProperty("items").GetArrayLength());
        Assert.IsFalse(result.GetProperty("hasNextPage").GetBoolean());
        Assert.AreEqual(JsonValueKind.Null, result.GetProperty("endCursor").ValueKind);
    }

    [DataTestMethod]
    [DataRow(int.MaxValue - 1)]
    [DataRow(int.MaxValue)]
    public async Task LargePageSizeReturnsAllGroupsWithoutOverflow(int first)
    {
        JsonElement result = await SuccessAsync(new()
        {
            ["function"] = "count",
            ["groupby"] = new[] { "publisher_id" },
            ["orderby"] = "desc",
            ["first"] = first
        });
        CollectionAssert.AreEqual(new[] { 1234, 2345 }, result.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("publisher_id").GetInt32()).ToArray());
        Assert.IsFalse(result.GetProperty("hasNextPage").GetBoolean());
        Assert.AreEqual("Mg==", result.GetProperty("endCursor").GetString());
    }

    [TestMethod]
    public async Task DatabasePolicyRestrictsRowsBeforeAggregation()
    {
        EntityAction permission = new(EntityActionOperation.Read, new(new()), new(Database: "@item.publisher_id eq 1234"));
        JsonElement rows = await SuccessAsync(new() { ["function"] = "sum", ["field"] = "id" }, permission);
        Assert.AreEqual(3m, rows[0].GetProperty("sum_id").GetDecimal());
    }

    [DataTestMethod]
    [DataRow("aggregate")]
    [DataRow("group")]
    [DataRow("filter")]
    public async Task FieldPermissionsPreventAggregationGroupingAndFiltering(string restrictedOperation)
    {
        // Permission field lists use backing column names; MCP arguments use exposed names.
        EntityAction permission = new(EntityActionOperation.Read, new(new() { "PUBLISHER_ID" }), null);
        Dictionary<string, object?> args = new() { ["function"] = "sum", ["field"] = restrictedOperation == "aggregate" ? "publisher_id" : "id" };
        if (restrictedOperation == "group")
        {
            args["groupby"] = new[] { "publisher_id" };
        }
        else if (restrictedOperation == "filter")
        {
            args["filter"] = "publisher_id eq 1234";
        }

        CallToolResult result = await AggregateAsync(args, permission);
        AssertError(result, "PermissionDenied");
        JsonElement allowed = await SuccessAsync(new() { ["function"] = "sum", ["field"] = "id" }, permission);
        Assert.AreEqual(6m, allowed[0].GetProperty("sum_id").GetDecimal());
    }

    [TestMethod]
    public async Task MissingReadPermissionPreventsAggregation()
    {
        CallToolResult result = await AggregateAsync(new() { ["function"] = "count" }, new(EntityActionOperation.Create, null, null));
        AssertError(result, "permission");
    }

    private static Task ExecuteSqlAsync(string sql) => _queryExecutor.ExecuteQueryAsync<object>(
        sql, parameters: null, dataReaderHandler: null, dataSourceName: string.Empty);

    private static async Task<JsonElement> SuccessAsync(Dictionary<string, object?> args, EntityAction permission = null)
    {
        CallToolResult result = await AggregateAsync(args, permission);
        AssertSuccess(result, "Oracle aggregation failed.");
        return ParseResultRoot(result).GetProperty("result");
    }

    private static async Task<CallToolResult> AggregateAsync(Dictionary<string, object?> args, EntityAction permission = null)
    {
        RuntimeConfig config = _application.Services.GetRequiredService<RuntimeConfigProvider>().GetConfig();
        if (permission is not null)
        {
            Dictionary<string, Entity> entities = new(config.Entities)
            {
                ["Book"] = config.Entities["Book"] with { Permissions = new[] { new EntityPermission("anonymous", new[] { permission }) } }
            };
            config = config with { Entities = new(entities) };
        }

        config = config with { Runtime = config.Runtime with { Mcp = new(Enabled: true) } };
        RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
        ServiceCollection services = new();
        services.AddSingleton(configProvider);
        services.AddSingleton(_metadataProviderFactory.Object);
        IHttpContextAccessor contextAccessor = CreateAnonymousHttpContextAccessor();
        services.AddSingleton(contextAccessor);
        services.AddSingleton<IAbstractQueryManagerFactory>(new QueryManagerFactory(
            configProvider, NullLogger<IQueryExecutor>.Instance, contextAccessor, null));
        services.AddSingleton<IAuthorizationResolver>(new AuthorizationResolver(configProvider, _metadataProviderFactory.Object));
        services.AddSingleton(new GQLFilterParser(configProvider, _metadataProviderFactory.Object));
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, RestAuthorizationHandler>();
        using ServiceProvider provider = services.BuildServiceProvider();
        args["entity"] = "Book";
        return await ExecuteToolAsync(new AggregateRecordsTool(), provider, args);
    }
}
