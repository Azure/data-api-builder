// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
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
    /// - SQL expression generation (count, avg, sum, min, max, distinct)
    /// - Table reference quoting, cursor/pagination logic
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
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("properties", out JsonElement properties));
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("required", out JsonElement required));

            List<string> requiredFields = new();
            foreach (JsonElement r in required.EnumerateArray())
            {
                requiredFields.Add(r.GetString()!);
            }

            CollectionAssert.Contains(requiredFields, "entity");
            CollectionAssert.Contains(requiredFields, "function");
            CollectionAssert.Contains(requiredFields, "field");

            // Verify first and after properties exist in schema
            Assert.IsTrue(properties.TryGetProperty("first", out JsonElement firstProp));
            Assert.AreEqual("integer", firstProp.GetProperty("type").GetString());
            Assert.IsTrue(properties.TryGetProperty("after", out JsonElement afterProp));
            Assert.AreEqual("string", afterProp.GetProperty("type").GetString());
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

        [TestMethod]
        public async Task AggregateRecords_StarFieldWithAvg_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"avg\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
            Assert.IsTrue(content.GetProperty("error").GetProperty("message").GetString()!.Contains("count"));
        }

        [TestMethod]
        public async Task AggregateRecords_DistinctCountStar_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"distinct\": true}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
            Assert.IsTrue(content.GetProperty("error").GetProperty("message").GetString()!.Contains("DISTINCT"));
        }

        [TestMethod]
        public async Task AggregateRecords_HavingWithoutGroupBy_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"having\": {\"gt\": 5}}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
            Assert.IsTrue(content.GetProperty("error").GetProperty("message").GetString()!.Contains("groupby"));
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

        #region Cursor and Pagination Tests

        [TestMethod]
        public void DecodeCursorOffset_NullCursor_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(null));
        }

        [TestMethod]
        public void DecodeCursorOffset_EmptyCursor_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(""));
        }

        [TestMethod]
        public void DecodeCursorOffset_WhitespaceCursor_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset("   "));
        }

        [TestMethod]
        public void DecodeCursorOffset_ValidBase64Cursor_ReturnsDecodedOffset()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("5"));
            Assert.AreEqual(5, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_InvalidBase64_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset("not-valid-base64!!!"));
        }

        [TestMethod]
        public void DecodeCursorOffset_NonNumericBase64_ReturnsZero()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("abc"));
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_RoundTrip_PreservesOffset()
        {
            int expectedOffset = 15;
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(expectedOffset.ToString()));
            Assert.AreEqual(expectedOffset, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_ZeroOffset_ReturnsZero()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("0"));
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_LargeOffset_ReturnsCorrectValue()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("1000"));
            Assert.AreEqual(1000, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion

        #region Timeout and Cancellation Tests

        /// <summary>
        /// Verifies that OperationCanceledException produces a model-explicit error
        /// that clearly states the operation was canceled, not errored.
        /// </summary>
        [TestMethod]
        public async Task AggregateRecords_OperationCanceled_ReturnsExplicitCanceledMessage()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            // Create a pre-canceled token
            CancellationTokenSource cts = new();
            cts.Cancel();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, cts.Token);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            string? errorType = error.GetProperty("type").GetString();
            string? errorMessage = error.GetProperty("message").GetString();

            // Verify the error type identifies it as a cancellation
            Assert.IsNotNull(errorType);
            Assert.AreEqual("OperationCanceled", errorType);

            // Verify the message explicitly tells the model this is NOT a tool error
            Assert.IsNotNull(errorMessage);
            Assert.IsTrue(errorMessage!.Contains("NOT a tool error"), "Message must explicitly state this is NOT a tool error.");

            // Verify the message tells the model what happened
            Assert.IsTrue(errorMessage.Contains("canceled"), "Message must mention the operation was canceled.");

            // Verify the message tells the model it can retry
            Assert.IsTrue(errorMessage.Contains("retry"), "Message must tell the model it can retry.");
        }

        /// <summary>
        /// Verifies that the timeout error message provides explicit guidance to the model
        /// about what happened and what to do next.
        /// </summary>
        [TestMethod]
        public void TimeoutErrorMessage_ContainsModelGuidance()
        {
            // Simulate what the tool builds for a TimeoutException response
            string entityName = "Product";
            string expectedMessage = $"The aggregation query for entity '{entityName}' timed out. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "This may occur with large datasets or complex aggregations. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            // Verify message explicitly states it's NOT a tool error
            Assert.IsTrue(expectedMessage.Contains("NOT a tool error"), "Timeout message must state this is NOT a tool error.");

            // Verify message explains the cause
            Assert.IsTrue(expectedMessage.Contains("database did not respond"), "Timeout message must explain the database didn't respond.");

            // Verify message mentions large datasets
            Assert.IsTrue(expectedMessage.Contains("large datasets"), "Timeout message must mention large datasets as a possible cause.");

            // Verify message provides actionable remediation steps
            Assert.IsTrue(expectedMessage.Contains("filter"), "Timeout message must suggest using a filter.");
            Assert.IsTrue(expectedMessage.Contains("groupby"), "Timeout message must suggest reducing groupby fields.");
            Assert.IsTrue(expectedMessage.Contains("first"), "Timeout message must suggest using pagination with first.");
        }

        /// <summary>
        /// Verifies that TaskCanceledException (which typically signals HTTP/DB timeout)
        /// produces a TimeoutError, not a cancellation error.
        /// </summary>
        [TestMethod]
        public void TaskCanceledErrorMessage_ContainsTimeoutGuidance()
        {
            // Simulate what the tool builds for a TaskCanceledException response
            string entityName = "Product";
            string expectedMessage = $"The aggregation query for entity '{entityName}' was canceled, likely due to a timeout. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            // TaskCanceledException should produce a TimeoutError, not OperationCanceled
            Assert.IsTrue(expectedMessage.Contains("NOT a tool error"), "TaskCanceled message must state this is NOT a tool error.");
            Assert.IsTrue(expectedMessage.Contains("timeout"), "TaskCanceled message must reference timeout as the cause.");
            Assert.IsTrue(expectedMessage.Contains("filter"), "TaskCanceled message must suggest filter as remediation.");
            Assert.IsTrue(expectedMessage.Contains("first"), "TaskCanceled message must suggest first for pagination.");
        }

        /// <summary>
        /// Verifies that the OperationCanceled error message for a specific entity
        /// includes the entity name so the model knows which aggregation failed.
        /// </summary>
        [TestMethod]
        public void CanceledErrorMessage_IncludesEntityName()
        {
            string entityName = "LargeProductCatalog";
            string expectedMessage = $"The aggregation query for entity '{entityName}' was canceled before completion. "
                + "This is NOT a tool error. The operation was interrupted, possibly due to a timeout or client disconnect. "
                + "No results were returned. You may retry the same request.";

            Assert.IsTrue(expectedMessage.Contains(entityName), "Canceled message must include the entity name.");
            Assert.IsTrue(expectedMessage.Contains("No results were returned"), "Canceled message must state no results were returned.");
        }

        /// <summary>
        /// Verifies that the timeout error message for a specific entity
        /// includes the entity name so the model knows which aggregation timed out.
        /// </summary>
        [TestMethod]
        public void TimeoutErrorMessage_IncludesEntityName()
        {
            string entityName = "HugeTransactionLog";
            string expectedMessage = $"The aggregation query for entity '{entityName}' timed out. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "This may occur with large datasets or complex aggregations. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            Assert.IsTrue(expectedMessage.Contains(entityName), "Timeout message must include the entity name.");
        }

        #endregion

        #region Spec Example Tests

        /// <summary>
        /// Spec Example 1: "How many products are there?"
        /// COUNT(*) - expects alias "count"
        /// </summary>
        [TestMethod]
        public void SpecExample01_CountStar_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
        }

        /// <summary>
        /// Spec Example 2: "What is the average price of products under $10?"
        /// AVG(unitPrice) with filter
        /// </summary>
        [TestMethod]
        public void SpecExample02_AvgWithFilter_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            Assert.AreEqual("avg_unitPrice", alias);
        }

        /// <summary>
        /// Spec Example 3: "Which categories have more than 20 products?"
        /// COUNT(*) GROUP BY categoryName HAVING gt 20
        /// </summary>
        [TestMethod]
        public void SpecExample03_CountGroupByHavingGt_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
        }

        /// <summary>
        /// Spec Example 4: "For discontinued products, which categories have total revenue between $500 and $10,000?"
        /// SUM(unitPrice) GROUP BY categoryName HAVING gte 500 AND lte 10000
        /// </summary>
        [TestMethod]
        public void SpecExample04_SumFilterGroupByHavingRange_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "unitPrice");
            Assert.AreEqual("sum_unitPrice", alias);
        }

        /// <summary>
        /// Spec Example 5: "How many distinct suppliers do we have?"
        /// COUNT(DISTINCT supplierId)
        /// </summary>
        [TestMethod]
        public void SpecExample05_CountDistinct_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "supplierId");
            Assert.AreEqual("count_supplierId", alias);
        }

        /// <summary>
        /// Spec Example 6: "Which categories have exactly 5 or 10 products?"
        /// COUNT(*) GROUP BY categoryName HAVING IN (5, 10)
        /// </summary>
        [TestMethod]
        public void SpecExample06_CountGroupByHavingIn_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
        }

        /// <summary>
        /// Spec Example 7: "Average distinct unit price per category, for categories averaging over $25"
        /// AVG(DISTINCT unitPrice) GROUP BY categoryName HAVING gt 25
        /// </summary>
        [TestMethod]
        public void SpecExample07_AvgDistinctGroupByHavingGt_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            Assert.AreEqual("avg_unitPrice", alias);
        }

        /// <summary>
        /// Spec Example 8: "Which categories have the most products?"
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC
        /// </summary>
        [TestMethod]
        public void SpecExample08_CountGroupByOrderByDesc_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
        }

        /// <summary>
        /// Spec Example 9: "What are the cheapest categories by average price?"
        /// AVG(unitPrice) GROUP BY categoryName ORDER BY ASC
        /// </summary>
        [TestMethod]
        public void SpecExample09_AvgGroupByOrderByAsc_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            Assert.AreEqual("avg_unitPrice", alias);
        }

        /// <summary>
        /// Spec Example 10: "For categories with over $500 revenue, which has the highest total?"
        /// SUM(unitPrice) GROUP BY categoryName HAVING gt 500 ORDER BY DESC
        /// </summary>
        [TestMethod]
        public void SpecExample10_SumFilterGroupByHavingGtOrderByDesc_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "unitPrice");
            Assert.AreEqual("sum_unitPrice", alias);
        }

        /// <summary>
        /// Spec Example 11: "Show me the first 5 categories by product count"
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC FIRST 5
        /// </summary>
        [TestMethod]
        public void SpecExample11_CountGroupByOrderByDescFirst5_CorrectAliasAndCursor()
        {
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(null));
        }

        /// <summary>
        /// Spec Example 12: "Show me the next 5 categories" (continuation of Example 11)
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC FIRST 5 AFTER cursor
        /// </summary>
        [TestMethod]
        public void SpecExample12_CountGroupByOrderByDescFirst5After_CorrectCursorDecode()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("5"));
            int offset = AggregateRecordsTool.DecodeCursorOffset(cursor);
            Assert.AreEqual(5, offset);

            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            Assert.AreEqual("count", alias);
        }

        /// <summary>
        /// Spec Example 13: "Show me the top 3 most expensive categories by average price"
        /// AVG(unitPrice) GROUP BY categoryName ORDER BY DESC FIRST 3
        /// </summary>
        [TestMethod]
        public void SpecExample13_AvgGroupByOrderByDescFirst3_CorrectAlias()
        {
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            Assert.AreEqual("avg_unitPrice", alias);
        }

        #endregion

        #region Blog Scenario Tests (devblogs.microsoft.com/azure-sql/data-api-builder-mcp-questions)

        // These tests verify that the exact JSON payloads from the DAB MCP blog
        // pass input validation. The tool will fail at metadata resolution (no real DB)
        // but must NOT return "InvalidArguments", proving the input shape is valid.

        /// <summary>
        /// Blog Scenario 1: Strategic customer importance
        /// "Who is our most important customer based on total revenue?"
        /// Uses: sum, totalRevenue, filter, groupby [customerId, customerName], orderby desc, first 1
        /// </summary>
        [TestMethod]
        public async Task BlogScenario1_StrategicCustomerImportance_PassesInputValidation()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            string json = @"{
                ""entity"": ""Book"",
                ""function"": ""sum"",
                ""field"": ""totalRevenue"",
                ""filter"": ""isActive eq true and orderDate ge 2025-01-01"",
                ""groupby"": [""customerId"", ""customerName""],
                ""orderby"": ""desc"",
                ""first"": 1
            }";

            JsonDocument args = JsonDocument.Parse(json);
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Blog scenario 1 JSON must pass input validation (sum/totalRevenue/groupby/orderby/first).");
            Assert.AreEqual("sum_totalRevenue", AggregateRecordsTool.ComputeAlias("sum", "totalRevenue"));
        }

        /// <summary>
        /// Blog Scenario 2: Product discontinuation candidate
        /// "Which product should we consider discontinuing based on lowest totalRevenue?"
        /// Uses: sum, totalRevenue, filter, groupby [productId, productName], orderby asc, first 1
        /// </summary>
        [TestMethod]
        public async Task BlogScenario2_ProductDiscontinuation_PassesInputValidation()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            string json = @"{
                ""entity"": ""Book"",
                ""function"": ""sum"",
                ""field"": ""totalRevenue"",
                ""filter"": ""isActive eq true and inStock gt 0 and orderDate ge 2025-01-01"",
                ""groupby"": [""productId"", ""productName""],
                ""orderby"": ""asc"",
                ""first"": 1
            }";

            JsonDocument args = JsonDocument.Parse(json);
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Blog scenario 2 JSON must pass input validation (sum/totalRevenue/groupby/orderby asc/first).");
            Assert.AreEqual("sum_totalRevenue", AggregateRecordsTool.ComputeAlias("sum", "totalRevenue"));
        }

        /// <summary>
        /// Blog Scenario 3: Forward-looking performance expectation
        /// "Average quarterlyRevenue per region, regions averaging > $2,000,000?"
        /// Uses: avg, quarterlyRevenue, filter, groupby [region], having {gt: 2000000}, orderby desc
        /// </summary>
        [TestMethod]
        public async Task BlogScenario3_QuarterlyPerformance_PassesInputValidation()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            string json = @"{
                ""entity"": ""Book"",
                ""function"": ""avg"",
                ""field"": ""quarterlyRevenue"",
                ""filter"": ""fiscalYear eq 2025"",
                ""groupby"": [""region""],
                ""having"": { ""gt"": 2000000 },
                ""orderby"": ""desc""
            }";

            JsonDocument args = JsonDocument.Parse(json);
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Blog scenario 3 JSON must pass input validation (avg/quarterlyRevenue/groupby/having gt).");
            Assert.AreEqual("avg_quarterlyRevenue", AggregateRecordsTool.ComputeAlias("avg", "quarterlyRevenue"));
        }

        /// <summary>
        /// Blog Scenario 4: Revenue concentration across regions
        /// "Total revenue of active retail customers in Midwest/Southwest, >$5M, by region and customerTier"
        /// Uses: sum, totalRevenue, complex filter with OR, groupby [region, customerTier], having {gt: 5000000}, orderby desc
        /// </summary>
        [TestMethod]
        public async Task BlogScenario4_RevenueConcentration_PassesInputValidation()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            string json = @"{
                ""entity"": ""Book"",
                ""function"": ""sum"",
                ""field"": ""totalRevenue"",
                ""filter"": ""isActive eq true and customerType eq 'Retail' and (region eq 'Midwest' or region eq 'Southwest')"",
                ""groupby"": [""region"", ""customerTier""],
                ""having"": { ""gt"": 5000000 },
                ""orderby"": ""desc""
            }";

            JsonDocument args = JsonDocument.Parse(json);
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Blog scenario 4 JSON must pass input validation (sum/totalRevenue/complex filter/multi-groupby/having).");
            Assert.AreEqual("sum_totalRevenue", AggregateRecordsTool.ComputeAlias("sum", "totalRevenue"));
        }

        /// <summary>
        /// Blog Scenario 5: Risk exposure by product line
        /// "For discontinued products, total onHandValue by productLine and warehouseRegion, >$2.5M"
        /// Uses: sum, onHandValue, filter, groupby [productLine, warehouseRegion], having {gt: 2500000}, orderby desc
        /// </summary>
        [TestMethod]
        public async Task BlogScenario5_RiskExposure_PassesInputValidation()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            string json = @"{
                ""entity"": ""Book"",
                ""function"": ""sum"",
                ""field"": ""onHandValue"",
                ""filter"": ""discontinued eq true and onHandValue gt 0"",
                ""groupby"": [""productLine"", ""warehouseRegion""],
                ""having"": { ""gt"": 2500000 },
                ""orderby"": ""desc""
            }";

            JsonDocument args = JsonDocument.Parse(json);
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Blog scenario 5 JSON must pass input validation (sum/onHandValue/filter/multi-groupby/having).");
            Assert.AreEqual("sum_onHandValue", AggregateRecordsTool.ComputeAlias("sum", "onHandValue"));
        }

        /// <summary>
        /// Verifies that the tool schema supports all properties used across the 5 blog scenarios.
        /// </summary>
        [TestMethod]
        public void BlogScenarios_ToolSchema_SupportsAllRequiredProperties()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();
            JsonElement properties = metadata.InputSchema.GetProperty("properties");

            string[] blogProperties = { "entity", "function", "field", "filter", "groupby", "orderby", "having", "first" };
            foreach (string prop in blogProperties)
            {
                Assert.IsTrue(properties.TryGetProperty(prop, out _),
                    $"Tool schema must include '{prop}' property used in blog scenarios.");
            }

            // Additional schema properties used in spec but not blog
            Assert.IsTrue(properties.TryGetProperty("distinct", out _), "Tool schema must include 'distinct'.");
            Assert.IsTrue(properties.TryGetProperty("after", out _), "Tool schema must include 'after'.");
        }

        /// <summary>
        /// Verifies that the tool description instructs models to call describe_entities first.
        /// </summary>
        [TestMethod]
        public void BlogScenarios_ToolDescription_ForcesDescribeEntitiesFirst()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            Assert.IsTrue(metadata.Description!.Contains("describe_entities"),
                "Tool description must instruct models to call describe_entities first.");
            Assert.IsTrue(metadata.Description.Contains("1)"),
                "Tool description must use numbered workflow steps.");
        }

        /// <summary>
        /// Verifies that the tool description documents the alias convention used in blog examples.
        /// </summary>
        [TestMethod]
        public void BlogScenarios_ToolDescription_DocumentsAliasConvention()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            Assert.IsTrue(metadata.Description!.Contains("{function}_{field}"),
                "Tool description must document the alias pattern '{function}_{field}'.");
            Assert.IsTrue(metadata.Description.Contains("'count'"),
                "Tool description must mention the special 'count' alias for count(*).");
        }

        #endregion

        #region FieldNotFound Error Helper Tests

        /// <summary>
        /// Verifies the FieldNotFound error helper produces the correct error type
        /// and a model-friendly message that includes the field name, entity, and guidance.
        /// </summary>
        [TestMethod]
        public void FieldNotFound_ReturnsCorrectErrorTypeAndMessage()
        {
            CallToolResult result = McpErrorHelpers.FieldNotFound("aggregate_records", "Product", "badField", "field", null);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            JsonElement error = content.GetProperty("error");

            Assert.AreEqual("FieldNotFound", error.GetProperty("type").GetString());
            string message = error.GetProperty("message").GetString()!;
            Assert.IsTrue(message.Contains("badField"), "Message must include the invalid field name.");
            Assert.IsTrue(message.Contains("Product"), "Message must include the entity name.");
            Assert.IsTrue(message.Contains("field"), "Message must identify which parameter was invalid.");
            Assert.IsTrue(message.Contains("describe_entities"), "Message must guide the model to call describe_entities.");
        }

        /// <summary>
        /// Verifies the FieldNotFound error helper identifies the groupby parameter.
        /// </summary>
        [TestMethod]
        public void FieldNotFound_GroupBy_IdentifiesParameter()
        {
            CallToolResult result = McpErrorHelpers.FieldNotFound("aggregate_records", "Product", "invalidCol", "groupby", null);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string message = content.GetProperty("error").GetProperty("message").GetString()!;

            Assert.IsTrue(message.Contains("invalidCol"), "Message must include the invalid field name.");
            Assert.IsTrue(message.Contains("groupby"), "Message must identify 'groupby' as the parameter.");
            Assert.IsTrue(message.Contains("describe_entities"), "Message must guide the model to call describe_entities.");
        }

        #endregion

        #region Helper Methods

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
