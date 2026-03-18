// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// Integration tests for the AggregateRecordsTool MCP tool.
    /// Covers tool metadata/schema, configuration, input validation,
    /// alias conventions, cursor/pagination, timeout/cancellation, spec examples,
    /// and blog scenario validation.
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region Tool Metadata Tests

        [TestMethod]
        public void GetToolMetadata_ReturnsCorrectNameAndType()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            Assert.AreEqual("aggregate_records", metadata.Name);
            Assert.AreEqual(McpEnums.ToolType.BuiltIn, tool.ToolType);
        }

        [TestMethod]
        public void GetToolMetadata_HasRequiredSchemaProperties()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            Assert.AreEqual(JsonValueKind.Object, metadata.InputSchema.ValueKind);
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("properties", out JsonElement properties));
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("required", out JsonElement required));

            // Verify required fields
            List<string> requiredFields = new();
            foreach (JsonElement r in required.EnumerateArray())
            {
                requiredFields.Add(r.GetString()!);
            }

            CollectionAssert.Contains(requiredFields, "entity");
            CollectionAssert.Contains(requiredFields, "function");
            CollectionAssert.Contains(requiredFields, "field");

            // Verify all schema properties exist with correct types
            AssertSchemaProperty(properties, "entity", "string");
            AssertSchemaProperty(properties, "function", "string");
            AssertSchemaProperty(properties, "field", "string");
            AssertSchemaProperty(properties, "distinct", "boolean");
            AssertSchemaProperty(properties, "filter", "string");
            AssertSchemaProperty(properties, "groupby", "array");
            AssertSchemaProperty(properties, "orderby", "string");
            AssertSchemaProperty(properties, "having", "object");
            AssertSchemaProperty(properties, "first", "integer");
            AssertSchemaProperty(properties, "after", "string");
        }

        [TestMethod]
        public void GetToolMetadata_DescriptionDocumentsWorkflowAndAlias()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            Assert.IsTrue(metadata.Description!.Contains("describe_entities"),
                "Tool description must instruct models to call describe_entities first.");
            Assert.IsTrue(metadata.Description.Contains("1)"),
                "Tool description must use numbered workflow steps.");
            Assert.IsTrue(metadata.Description.Contains("{function}_{field}"),
                "Tool description must document the alias pattern '{function}_{field}'.");
            Assert.IsTrue(metadata.Description.Contains("'count'"),
                "Tool description must mention the special 'count' alias for count(*).");
        }

        #endregion

        #region Configuration Tests

        [DataTestMethod]
        [DataRow(false, true, DisplayName = "Runtime-level disabled")]
        [DataRow(true, false, DisplayName = "Entity-level DML disabled")]
        public async Task AggregateRecords_Disabled_ReturnsToolDisabledError(bool runtimeEnabled, bool entityDmlEnabled)
        {
            RuntimeConfig config = entityDmlEnabled
                ? CreateConfig(aggregateRecordsEnabled: runtimeEnabled)
                : CreateConfigWithEntityDmlDisabled();
            IServiceProvider sp = CreateServiceProvider(config);

            CallToolResult result = await ExecuteToolAsync(sp, "{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");

            AssertErrorResult(result, "ToolDisabled");
        }

        #endregion

        #region Input Validation Tests - Missing/Invalid Arguments

        [DataTestMethod]
        [DataRow("{\"function\": \"count\", \"field\": \"*\"}", null, DisplayName = "Missing entity")]
        [DataRow("{\"entity\": \"Book\", \"field\": \"*\"}", null, DisplayName = "Missing function")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\"}", null, DisplayName = "Missing field")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"median\", \"field\": \"price\"}", "median", DisplayName = "Invalid function 'median'")]
        public async Task AggregateRecords_MissingOrInvalidRequiredArgs_ReturnsInvalidArguments(string json, string expectedInMessage)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            string message = AssertErrorResult(result, "InvalidArguments");
            if (!string.IsNullOrEmpty(expectedInMessage))
            {
                Assert.IsTrue(message.Contains(expectedInMessage),
                    $"Error message must contain '{expectedInMessage}'. Actual: '{message}'");
            }
        }

        [TestMethod]
        public async Task AggregateRecords_NullArguments_ReturnsInvalidArguments()
        {
            IServiceProvider sp = CreateDefaultServiceProvider();
            AggregateRecordsTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, sp, CancellationToken.None);

            AssertErrorResult(result, "InvalidArguments");
        }

        #endregion

        #region Input Validation Tests - Field/Function Compatibility

        [DataTestMethod]
        [DataRow("{\"entity\": \"Book\", \"function\": \"avg\", \"field\": \"*\"}", "count",
            DisplayName = "Star field with avg (must mention count)")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"distinct\": true}", "DISTINCT",
            DisplayName = "Distinct with count(*)")]
        public async Task AggregateRecords_InvalidFieldFunctionCombination_ReturnsInvalidArguments(string json, string expectedInMessage)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            string message = AssertErrorResult(result, "InvalidArguments");
            Assert.IsTrue(message.Contains(expectedInMessage),
                $"Error message must contain '{expectedInMessage}'. Actual: '{message}'");
        }

        #endregion

        #region Input Validation Tests - GroupBy Dependencies

        [DataTestMethod]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"having\": {\"gt\": 5}}", "groupby",
            DisplayName = "Having without groupby")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"groupby\": [\"title\"], \"orderby\": \"ascending\"}", "'asc' or 'desc'",
            DisplayName = "Invalid orderby value")]
        public async Task AggregateRecords_GroupByDependencyViolation_ReturnsInvalidArguments(string json, string expectedInMessage)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            string message = AssertErrorResult(result, "InvalidArguments");
            Assert.IsTrue(message.Contains(expectedInMessage),
                $"Error message must contain '{expectedInMessage}'. Actual: '{message}'");
        }

        #endregion

        #region Input Validation Tests - Orderby Without Groupby (Issue #3279)

        /// <summary>
        /// Verifies that orderby without groupby is silently ignored rather than rejected.
        /// This is the core fix for https://github.com/Azure/data-api-builder/issues/3279.
        /// The tool should pass input validation and only fail at metadata resolution (no real DB).
        /// </summary>
        [DataTestMethod]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"orderby\": \"desc\"}",
            DisplayName = "count(*) with orderby desc, no groupby")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"orderby\": \"asc\"}",
            DisplayName = "count(*) with orderby asc, no groupby")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"avg\", \"field\": \"price\", \"orderby\": \"desc\"}",
            DisplayName = "avg(price) with orderby desc, no groupby")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"sum\", \"field\": \"price\", \"orderby\": \"asc\"}",
            DisplayName = "sum(price) with orderby asc, no groupby")]
        public async Task AggregateRecords_OrderbyWithoutGroupby_PassesValidation(string json)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            // The tool will fail at metadata resolution (no real DB), but must NOT fail with InvalidArguments.
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                $"orderby without groupby must not be rejected as InvalidArguments. Got error type: {errorType}");
        }

        /// <summary>
        /// Verifies that simple count(*) without orderby or groupby passes validation.
        /// </summary>
        [TestMethod]
        public async Task AggregateRecords_SimpleCountStar_PassesValidation()
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp,
                "{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                "Simple count(*) must pass input validation.");
        }

        /// <summary>
        /// Verifies ValidateGroupByDependencies directly: orderby is silently cleared
        /// when groupby count is 0.
        /// </summary>
        [TestMethod]
        public void ValidateGroupByDependencies_OrderbyWithoutGroupby_ClearsOrderbyFlag()
        {
            bool userProvidedOrderby = true;
            CallToolResult? result = AggregateRecordsTool.ValidateGroupByDependencies(
                groupbyCount: 0,
                userProvidedOrderby: ref userProvidedOrderby,
                first: null,
                after: null,
                toolName: "aggregate_records",
                logger: null);

            Assert.IsNull(result, "No error should be returned when orderby is provided without groupby.");
            Assert.IsFalse(userProvidedOrderby, "userProvidedOrderby must be set to false when groupby is absent.");
        }

        /// <summary>
        /// Verifies ValidateGroupByDependencies preserves orderby when groupby is present.
        /// </summary>
        [TestMethod]
        public void ValidateGroupByDependencies_OrderbyWithGroupby_PreservesOrderbyFlag()
        {
            bool userProvidedOrderby = true;
            CallToolResult? result = AggregateRecordsTool.ValidateGroupByDependencies(
                groupbyCount: 2,
                userProvidedOrderby: ref userProvidedOrderby,
                first: null,
                after: null,
                toolName: "aggregate_records",
                logger: null);

            Assert.IsNull(result, "No error should be returned when both orderby and groupby are provided.");
            Assert.IsTrue(userProvidedOrderby, "userProvidedOrderby must remain true when groupby is present.");
        }

        /// <summary>
        /// Verifies that the orderby schema property has no default value (fix for #3279).
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_OrderbySchemaHasNoDefault()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();

            JsonElement properties = metadata.InputSchema.GetProperty("properties");
            JsonElement orderbyProp = properties.GetProperty("orderby");

            Assert.IsFalse(orderbyProp.TryGetProperty("default", out _),
                "The 'orderby' schema property must not have a default value. " +
                "A default causes LLMs to always send orderby, which previously forced groupby to be required.");
        }

        #endregion

        #region Input Validation Tests - Having Clause

        [DataTestMethod]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"groupby\": [\"title\"], \"having\": {\"between\": 5}}",
            "between", DisplayName = "Unsupported having operator")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"groupby\": [\"title\"], \"having\": {\"eq\": \"ten\"}}",
            "numeric", DisplayName = "Non-numeric having scalar")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"groupby\": [\"title\"], \"having\": {\"in\": [5, \"abc\"]}}",
            "numeric", DisplayName = "Non-numeric value in having.in array")]
        [DataRow("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\", \"groupby\": [\"title\"], \"having\": {\"in\": 5}}",
            "numeric array", DisplayName = "Having.in not an array")]
        public async Task AggregateRecords_InvalidHaving_ReturnsInvalidArguments(string json, string expectedInMessage)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            string message = AssertErrorResult(result, "InvalidArguments");
            Assert.IsTrue(message.Contains(expectedInMessage),
                $"Error message must contain '{expectedInMessage}'. Actual: '{message}'");
        }

        #endregion

        #region Alias Convention Tests

        [DataTestMethod]
        [DataRow("count", "*", "count", DisplayName = "count(*) → 'count'")]
        [DataRow("count", "supplierId", "count_supplierId", DisplayName = "count(supplierId)")]
        [DataRow("avg", "unitPrice", "avg_unitPrice", DisplayName = "avg(unitPrice)")]
        [DataRow("sum", "unitPrice", "sum_unitPrice", DisplayName = "sum(unitPrice)")]
        [DataRow("min", "price", "min_price", DisplayName = "min(price)")]
        [DataRow("max", "price", "max_price", DisplayName = "max(price)")]
        public void ComputeAlias_ReturnsExpectedAlias(string function, string field, string expectedAlias)
        {
            Assert.AreEqual(expectedAlias, AggregateRecordsTool.ComputeAlias(function, field));
        }

        #endregion

        #region Cursor and Pagination Tests

        [DataTestMethod]
        [DataRow(null, 0, DisplayName = "null → 0")]
        [DataRow("", 0, DisplayName = "empty → 0")]
        [DataRow("   ", 0, DisplayName = "whitespace → 0")]
        public void DecodeCursorOffset_InvalidCursor_ReturnsZero(string cursor, int expected)
        {
            Assert.AreEqual(expected, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_InvalidBase64_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset("not-valid-base64!!!"));
        }

        [DataTestMethod]
        [DataRow("abc", 0, DisplayName = "non-numeric → 0")]
        [DataRow("0", 0, DisplayName = "zero → 0")]
        [DataRow("5", 5, DisplayName = "5 round-trip")]
        [DataRow("15", 15, DisplayName = "15 round-trip")]
        [DataRow("1000", 1000, DisplayName = "1000 round-trip")]
        public void DecodeCursorOffset_Base64Encoded_ReturnsExpectedOffset(string rawValue, int expectedOffset)
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawValue));
            Assert.AreEqual(expectedOffset, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion

        #region Timeout and Cancellation Tests

        [TestMethod]
        public async Task AggregateRecords_OperationCanceled_ReturnsExplicitCanceledMessage()
        {
            IServiceProvider sp = CreateDefaultServiceProvider();
            AggregateRecordsTool tool = new();

            CancellationTokenSource cts = new();
            cts.Cancel();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, cts.Token);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            JsonElement error = content.GetProperty("error");

            Assert.AreEqual("OperationCanceled", error.GetProperty("type").GetString());
            string message = error.GetProperty("message").GetString()!;

            AssertContainsAll(message,
                ("NOT a tool error", "Message must state this is NOT a tool error."),
                ("canceled", "Message must mention the operation was canceled."),
                ("retry", "Message must tell the model it can retry."));
        }

        [DataTestMethod]
        [DataRow("Product", DisplayName = "Product entity")]
        [DataRow("HugeTransactionLog", DisplayName = "HugeTransactionLog entity")]
        public void BuildTimeoutErrorMessage_ContainsGuidance(string entityName)
        {
            string message = AggregateRecordsTool.BuildTimeoutErrorMessage(entityName);

            AssertContainsAll(message,
                (entityName, "Must include entity name."),
                ("NOT a tool error", "Must state this is NOT a tool error."),
                ("database did not respond", "Must explain the cause."),
                ("large datasets", "Must mention large datasets."),
                ("filter", "Must suggest filter."),
                ("groupby", "Must suggest reducing groupby."),
                ("first", "Must suggest pagination."));
        }

        [DataTestMethod]
        [DataRow("LargeProductCatalog", DisplayName = "LargeProductCatalog entity")]
        public void BuildOperationCanceledErrorMessage_ContainsGuidance(string entityName)
        {
            string message = AggregateRecordsTool.BuildOperationCanceledErrorMessage(entityName);

            AssertContainsAll(message,
                (entityName, "Must include entity name."),
                ("No results were returned", "Must state no results."));
        }

        #endregion

        #region Spec Example Tests - Alias Validation

        /// <summary>
        /// Validates the alias convention for all 13 spec examples.
        /// Examples that compute count(*) expect "count"; all others expect "function_field".
        /// </summary>
        [DataTestMethod]
        // Ex 1, 3, 6, 8, 11, 12: COUNT(*) → "count"
        [DataRow("count", "*", "count", DisplayName = "Spec 01/03/06/08/11/12: count(*) → 'count'")]
        // Ex 2, 7, 9, 13: AVG(unitPrice) → "avg_unitPrice"
        [DataRow("avg", "unitPrice", "avg_unitPrice", DisplayName = "Spec 02/07/09/13: avg(unitPrice)")]
        // Ex 4, 10: SUM(unitPrice) → "sum_unitPrice"
        [DataRow("sum", "unitPrice", "sum_unitPrice", DisplayName = "Spec 04/10: sum(unitPrice)")]
        // Ex 5: COUNT(supplierId) → "count_supplierId"
        [DataRow("count", "supplierId", "count_supplierId", DisplayName = "Spec 05: count(supplierId)")]
        public void SpecExamples_AliasConvention_IsCorrect(string function, string field, string expectedAlias)
        {
            Assert.AreEqual(expectedAlias, AggregateRecordsTool.ComputeAlias(function, field));
        }

        /// <summary>
        /// Spec Example 11-12: Cursor offset for first-page starts at 0, continuation decodes correctly.
        /// </summary>
        [TestMethod]
        public void SpecExample_PaginationCursor_DecodesCorrectly()
        {
            // First page: null cursor → offset 0
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(null));

            // Continuation: cursor encoding "5" → offset 5
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("5"));
            Assert.AreEqual(5, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion

        #region Blog Scenario Tests

        /// <summary>
        /// Validates that exact JSON payloads from the DAB MCP blog pass input validation.
        /// The tool will fail at metadata resolution (no real DB) but must NOT return "InvalidArguments".
        /// </summary>
        [DataTestMethod]
        [DataRow(
            @"{""entity"":""Book"",""function"":""sum"",""field"":""totalRevenue"",""filter"":""isActive eq true and orderDate ge 2025-01-01"",""groupby"":[""customerId"",""customerName""],""orderby"":""desc"",""first"":1}",
            "sum", "totalRevenue",
            DisplayName = "Blog 1: Strategic customer importance")]
        [DataRow(
            @"{""entity"":""Book"",""function"":""sum"",""field"":""totalRevenue"",""filter"":""isActive eq true and inStock gt 0 and orderDate ge 2025-01-01"",""groupby"":[""productId"",""productName""],""orderby"":""asc"",""first"":1}",
            "sum", "totalRevenue",
            DisplayName = "Blog 2: Product discontinuation")]
        [DataRow(
            @"{""entity"":""Book"",""function"":""avg"",""field"":""quarterlyRevenue"",""filter"":""fiscalYear eq 2025"",""groupby"":[""region""],""having"":{""gt"":2000000},""orderby"":""desc""}",
            "avg", "quarterlyRevenue",
            DisplayName = "Blog 3: Quarterly performance")]
        [DataRow(
            @"{""entity"":""Book"",""function"":""sum"",""field"":""totalRevenue"",""filter"":""isActive eq true and customerType eq 'Retail' and (region eq 'Midwest' or region eq 'Southwest')"",""groupby"":[""region"",""customerTier""],""having"":{""gt"":5000000},""orderby"":""desc""}",
            "sum", "totalRevenue",
            DisplayName = "Blog 4: Revenue concentration")]
        [DataRow(
            @"{""entity"":""Book"",""function"":""sum"",""field"":""onHandValue"",""filter"":""discontinued eq true and onHandValue gt 0"",""groupby"":[""productLine"",""warehouseRegion""],""having"":{""gt"":2500000},""orderby"":""desc""}",
            "sum", "onHandValue",
            DisplayName = "Blog 5: Risk exposure")]
        public async Task BlogScenario_PassesInputValidation(string json, string function, string field)
        {
            IServiceProvider sp = CreateDefaultServiceProvider();

            CallToolResult result = await ExecuteToolAsync(sp, json);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            string errorType = content.GetProperty("error").GetProperty("type").GetString()!;
            Assert.AreNotEqual("InvalidArguments", errorType,
                $"Blog scenario JSON must pass input validation. Got error: {errorType}");

            // Verify alias convention
            string expectedAlias = $"{function}_{field}";
            Assert.AreEqual(expectedAlias, AggregateRecordsTool.ComputeAlias(function, field));
        }

        #endregion

        #region FieldNotFound Error Helper Tests

        [DataTestMethod]
        [DataRow("Product", "badField", "field", DisplayName = "field parameter")]
        [DataRow("Product", "invalidCol", "groupby", DisplayName = "groupby parameter")]
        public void FieldNotFound_ReturnsCorrectErrorWithGuidance(string entity, string fieldName, string paramName)
        {
            CallToolResult result = McpErrorHelpers.FieldNotFound("aggregate_records", entity, fieldName, paramName, null);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            JsonElement error = content.GetProperty("error");

            Assert.AreEqual("FieldNotFound", error.GetProperty("type").GetString());
            string message = error.GetProperty("message").GetString()!;

            AssertContainsAll(message,
                (fieldName, "Must include the invalid field name."),
                (entity, "Must include the entity name."),
                (paramName, "Must identify which parameter was invalid."),
                ("describe_entities", "Must guide the model to call describe_entities."));
        }

        #endregion

        #region Reusable Assertion Helpers

        /// <summary>
        /// Parses the JSON content from a <see cref="CallToolResult"/>.
        /// </summary>
        private static JsonElement ParseContent(CallToolResult result)
        {
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        /// <summary>
        /// Asserts that the result is an error with the expected error type.
        /// Returns the error message for further assertions.
        /// </summary>
        private static string AssertErrorResult(CallToolResult result, string expectedErrorType)
        {
            Assert.IsTrue(result.IsError == true, "Result should be an error.");
            JsonElement content = ParseContent(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error), "Content must have an 'error' property.");
            Assert.AreEqual(expectedErrorType, error.GetProperty("type").GetString(),
                $"Expected error type '{expectedErrorType}'.");
            return error.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Asserts the schema property exists with the given type.
        /// </summary>
        private static void AssertSchemaProperty(JsonElement properties, string propertyName, string expectedType)
        {
            Assert.IsTrue(properties.TryGetProperty(propertyName, out JsonElement prop),
                $"Schema must include '{propertyName}' property.");
            Assert.AreEqual(expectedType, prop.GetProperty("type").GetString(),
                $"Schema property '{propertyName}' must have type '{expectedType}'.");
        }

        /// <summary>
        /// Asserts that the given text contains all expected substrings, with per-assertion failure messages.
        /// </summary>
        private static void AssertContainsAll(string text, params (string expected, string failMessage)[] checks)
        {
            Assert.IsNotNull(text);
            foreach (var (expected, failMessage) in checks)
            {
                Assert.IsTrue(text.Contains(expected), failMessage);
            }
        }

        #endregion

        #region Reusable Execution Helpers

        /// <summary>
        /// Executes the AggregateRecordsTool with the given JSON arguments.
        /// </summary>
        private static async Task<CallToolResult> ExecuteToolAsync(IServiceProvider sp, string json)
        {
            AggregateRecordsTool tool = new();
            JsonDocument args = JsonDocument.Parse(json);
            return await tool.ExecuteAsync(args, sp, CancellationToken.None);
        }

        /// <summary>
        /// Creates a default service provider with aggregate_records enabled.
        /// </summary>
        private static IServiceProvider CreateDefaultServiceProvider()
        {
            return CreateServiceProvider(CreateConfig());
        }

        #endregion

        #region Test Infrastructure

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
