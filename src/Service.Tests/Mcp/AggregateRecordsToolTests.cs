// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for the AggregateRecordsTool MCP tool.
    /// Covers:
    /// - Tool metadata and schema validation
    /// - Runtime-level enabled/disabled configuration
    /// - Entity-level DML tool configuration
    /// - Input validation (missing/invalid arguments)
    /// - In-memory aggregation logic (count, avg, sum, min, max)
    /// - distinct, groupby, having, orderby
    /// - Alias convention
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region Tool Metadata Tests

        [TestMethod]
        public void GetToolMetadata_ReturnsCorrectName()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();
            Assert.AreEqual("aggregate_records", metadata.Name);
        }

        [TestMethod]
        public void GetToolMetadata_ReturnsCorrectToolType()
        {
            AggregateRecordsTool tool = new();
            Assert.AreEqual(McpEnums.ToolType.BuiltIn, tool.ToolType);
        }

        [TestMethod]
        public void GetToolMetadata_HasInputSchema()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();
            Assert.AreEqual(JsonValueKind.Object, metadata.InputSchema.ValueKind);
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("properties", out _));
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("required", out JsonElement required));

            List<string> requiredFields = new();
            foreach (JsonElement r in required.EnumerateArray())
            {
                requiredFields.Add(r.GetString()!);
            }

            CollectionAssert.Contains(requiredFields, "entity");
            CollectionAssert.Contains(requiredFields, "function");
            CollectionAssert.Contains(requiredFields, "field");
        }

        #endregion

        #region Configuration Tests

        [TestMethod]
        public async Task AggregateRecords_DisabledAtRuntimeLevel_ReturnsToolDisabledError()
        {
            RuntimeConfig config = CreateConfig(aggregateRecordsEnabled: false);
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            AssertToolDisabledError(content);
        }

        [TestMethod]
        public async Task AggregateRecords_DisabledAtEntityLevel_ReturnsToolDisabledError()
        {
            RuntimeConfig config = CreateConfigWithEntityDmlDisabled();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            AssertToolDisabledError(content);
        }

        #endregion

        #region Input Validation Tests

        [TestMethod]
        public async Task AggregateRecords_NullArguments_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.AreEqual("InvalidArguments", error.GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingEntity_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingFunction_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingField_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_InvalidFunction_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"median\", \"field\": \"price\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
            Assert.IsTrue(content.GetProperty("error").GetProperty("message").GetString()!.Contains("median"));
        }

        #endregion

        #region Alias Convention Tests

        [TestMethod]
        public void ComputeAlias_CountStar_ReturnsCount()
        {
            Assert.AreEqual("count", AggregateRecordsTool.ComputeAlias("count", "*"));
        }

        [TestMethod]
        public void ComputeAlias_CountField_ReturnsFunctionField()
        {
            Assert.AreEqual("count_supplierId", AggregateRecordsTool.ComputeAlias("count", "supplierId"));
        }

        [TestMethod]
        public void ComputeAlias_AvgField_ReturnsFunctionField()
        {
            Assert.AreEqual("avg_unitPrice", AggregateRecordsTool.ComputeAlias("avg", "unitPrice"));
        }

        [TestMethod]
        public void ComputeAlias_SumField_ReturnsFunctionField()
        {
            Assert.AreEqual("sum_unitPrice", AggregateRecordsTool.ComputeAlias("sum", "unitPrice"));
        }

        [TestMethod]
        public void ComputeAlias_MinField_ReturnsFunctionField()
        {
            Assert.AreEqual("min_price", AggregateRecordsTool.ComputeAlias("min", "price"));
        }

        [TestMethod]
        public void ComputeAlias_MaxField_ReturnsFunctionField()
        {
            Assert.AreEqual("max_price", AggregateRecordsTool.ComputeAlias("max", "price"));
        }

        #endregion

        #region In-Memory Aggregation Tests

        [TestMethod]
        public void PerformAggregation_CountStar_ReturnsCount()
        {
            JsonElement records = ParseArray("[{\"id\":1},{\"id\":2},{\"id\":3}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.0, result[0]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_Avg_ReturnsAverage()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", false, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Sum_ReturnsSum()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), null, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(60.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Min_ReturnsMin()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "min", "price", false, new(), null, null, "desc", "min_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(5.0, result[0]["min_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Max_ReturnsMax()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "max", "price", false, new(), null, null, "desc", "max_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["max_price"]);
        }

        [TestMethod]
        public void PerformAggregation_CountDistinct_ReturnsDistinctCount()
        {
            JsonElement records = ParseArray("[{\"supplierId\":1},{\"supplierId\":2},{\"supplierId\":1},{\"supplierId\":3}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "supplierId", true, new(), null, null, "desc", "count_supplierId");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.0, result[0]["count_supplierId"]);
        }

        [TestMethod]
        public void PerformAggregation_AvgDistinct_ReturnsDistinctAvg()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", true, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_ReturnsGroupedResults()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"A\",\"price\":20},{\"category\":\"B\",\"price\":50}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, null, null, "desc", "sum_price");

            Assert.AreEqual(2, result.Count);
            // Desc order: B(50) first, then A(30)
            Assert.AreEqual("B", result[0]["category"]?.ToString());
            Assert.AreEqual(50.0, result[0]["sum_price"]);
            Assert.AreEqual("A", result[1]["category"]?.ToString());
            Assert.AreEqual(30.0, result[1]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_Asc_ReturnsSortedAsc()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":30},{\"category\":\"A\",\"price\":20}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, null, null, "asc", "sum_price");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
            Assert.AreEqual("B", result[1]["category"]?.ToString());
            Assert.AreEqual(30.0, result[1]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_CountStar_GroupBy_ReturnsGroupCounts()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\"},{\"category\":\"A\"},{\"category\":\"B\"}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "category" }, null, null, "desc", "count");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(2.0, result[0]["count"]);
            Assert.AreEqual("B", result[1]["category"]?.ToString());
            Assert.AreEqual(1.0, result[1]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingGt_FiltersResults()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"A\",\"price\":20},{\"category\":\"B\",\"price\":5}]");
            var having = new Dictionary<string, double> { ["gt"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingGteLte_FiltersRange()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":100},{\"category\":\"B\",\"price\":20},{\"category\":\"C\",\"price\":1}]");
            var having = new Dictionary<string, double> { ["gte"] = 10, ["lte"] = 50 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("B", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_HavingIn_FiltersExactValues()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\"},{\"category\":\"A\"},{\"category\":\"B\"},{\"category\":\"C\"},{\"category\":\"C\"},{\"category\":\"C\"}]");
            var havingIn = new List<double> { 2, 3 };
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "category" }, null, havingIn, "desc", "count");

            Assert.AreEqual(2, result.Count);
            // C(3) desc, A(2)
            Assert.AreEqual("C", result[0]["category"]?.ToString());
            Assert.AreEqual(3.0, result[0]["count"]);
            Assert.AreEqual("A", result[1]["category"]?.ToString());
            Assert.AreEqual(2.0, result[1]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingEq_FiltersSingleValue()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":20}]");
            var having = new Dictionary<string, double> { ["eq"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_HavingNeq_FiltersOutValue()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":20}]");
            var having = new Dictionary<string, double> { ["neq"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("B", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecords_ReturnsNull()
        {
            JsonElement records = ParseArray("[]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", false, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecordsCountStar_ReturnsZero()
        {
            JsonElement records = ParseArray("[]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0.0, result[0]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_MultipleGroupByFields_ReturnsCorrectGroups()
        {
            JsonElement records = ParseArray("[{\"cat\":\"A\",\"region\":\"East\",\"price\":10},{\"cat\":\"A\",\"region\":\"East\",\"price\":20},{\"cat\":\"A\",\"region\":\"West\",\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "cat", "region" }, null, null, "desc", "sum_price");

            Assert.AreEqual(2, result.Count);
            // (A,East)=30 desc, (A,West)=5
            Assert.AreEqual("A", result[0]["cat"]?.ToString());
            Assert.AreEqual("East", result[0]["region"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingNoResults_ReturnsEmpty()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10}]");
            var having = new Dictionary<string, double> { ["gt"] = 100 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void PerformAggregation_HavingOnSingleResult_Passes()
        {
            JsonElement records = ParseArray("[{\"price\":50},{\"price\":60}]");
            var having = new Dictionary<string, double> { ["gte"] = 100 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(110.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingOnSingleResult_Fails()
        {
            JsonElement records = ParseArray("[{\"price\":50},{\"price\":60}]");
            var having = new Dictionary<string, double> { ["gt"] = 200 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), having, null, "desc", "sum_price");

            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region Helper Methods

        private static JsonElement ParseArray(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        private static JsonElement ParseContent(CallToolResult result)
        {
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        private static void AssertToolDisabledError(JsonElement content)
        {
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        private static RuntimeConfig CreateConfig(bool aggregateRecordsEnabled = true)
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: aggregateRecordsEnabled
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static RuntimeConfig CreateConfigWithEntityDmlDisabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
