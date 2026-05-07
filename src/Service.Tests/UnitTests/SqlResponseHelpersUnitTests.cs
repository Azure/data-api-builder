// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Focused unit tests for <see cref="SqlResponseHelpers.FormatFindResult"/>.
    ///
    /// These tests pin the response envelope shape across the five paths the method takes:
    /// (1) empty result, (2) single object, (3) collection without next page,
    /// (4) collection with next page (REST), (5) collection with next page (MCP).
    ///
    /// Test 6 is a regression guard against confusing array-typed column values with the old
    /// pagination "shape sentinel" — it pins that a row containing an array-valued property
    /// (e.g. a SQL Server JSON array, vector, or other collection-typed column) is returned
    /// unmodified and is NOT misinterpreted as a pagination marker.
    /// </summary>
    [TestClass]
    public class SqlResponseHelpersUnitTests
    {
        private const string ENTITY_NAME = "Book";

        #region Tests

        /// <summary>
        /// An empty result set returns the standard envelope <c>{ "value": [] }</c> and no
        /// pagination metadata.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_EmptyArray_ReturnsValueOnlyEnvelope()
        {
            JsonElement input = ParseJson("[]");
            FindRequestContext context = CreateContext(fieldsToBeReturned: new List<string>());

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: Mock.Of<ISqlMetadataProvider>(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext());

            JsonElement envelope = SerializeValue(result);
            AssertHasNoPaginationFields(envelope);
            Assert.AreEqual(0, envelope.GetProperty("value").GetArrayLength());
        }

        /// <summary>
        /// A single-object result (FindById) is wrapped into <c>{ "value": [ { ... } ] }</c>
        /// with no pagination metadata.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_SingleObject_ReturnsValueOnlyEnvelope()
        {
            JsonElement input = ParseJson(@"{ ""id"": 1, ""title"": ""Dune"" }");
            FindRequestContext context = CreateContext(fieldsToBeReturned: new List<string> { "id", "title" });

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: Mock.Of<ISqlMetadataProvider>(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext());

            JsonElement envelope = SerializeValue(result);
            AssertHasNoPaginationFields(envelope);

            JsonElement value = envelope.GetProperty("value");
            Assert.AreEqual(1, value.GetArrayLength());
            Assert.AreEqual(1, value[0].GetProperty("id").GetInt32());
            Assert.AreEqual("Dune", value[0].GetProperty("title").GetString());
        }

        /// <summary>
        /// A collection result that does NOT exceed <c>$first</c> has no next page; the envelope
        /// is <c>{ "value": [ ... ] }</c> with no pagination metadata.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_CollectionWithoutNextPage_ReturnsValueOnlyEnvelope()
        {
            // first = 5, only 2 rows: HasNext is false.
            JsonElement input = ParseJson(@"[ { ""id"": 1 }, { ""id"": 2 } ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id" },
                first: 5);

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: Mock.Of<ISqlMetadataProvider>(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext());

            JsonElement envelope = SerializeValue(result);
            AssertHasNoPaginationFields(envelope);
            Assert.AreEqual(2, envelope.GetProperty("value").GetArrayLength());
        }

        /// <summary>
        /// REST: a collection with more rows than requested produces the
        /// <c>{ "value": [ ... ], "nextLink": "..." }</c> envelope and trims the +1 probe row.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_CollectionWithNextPage_Rest_ReturnsNextLinkEnvelope()
        {
            // first = 1, 2 rows: HasNext is true, last (probe) row is dropped.
            JsonElement input = ParseJson(@"[ { ""id"": 1 }, { ""id"": 2 } ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id" },
                first: 1);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            httpContext.Request.Path = "/api/Book";

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: CreateMetadataProviderWithIdPrimaryKey(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: httpContext);

            JsonElement envelope = SerializeValue(result);
            JsonElement value = envelope.GetProperty("value");
            Assert.AreEqual(1, value.GetArrayLength(), "Probe row should have been removed.");
            Assert.AreEqual(1, value[0].GetProperty("id").GetInt32());

            Assert.IsTrue(envelope.TryGetProperty("nextLink", out JsonElement nextLink),
                "REST paginated response must carry a 'nextLink' field.");
            Assert.IsTrue(nextLink.GetString()!.Contains("$after="), "nextLink should encode the $after cursor.");
            Assert.IsFalse(envelope.TryGetProperty("after", out _),
                "REST paginated response must NOT carry an 'after' field.");
        }

        /// <summary>
        /// MCP: a collection with more rows than requested produces the
        /// <c>{ "value": [ ... ], "after": "..." }</c> envelope and trims the +1 probe row.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_CollectionWithNextPage_Mcp_ReturnsAfterEnvelope()
        {
            JsonElement input = ParseJson(@"[ { ""id"": 1 }, { ""id"": 2 } ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id" },
                first: 1);

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: CreateMetadataProviderWithIdPrimaryKey(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext(),
                isMcpRequest: true);

            JsonElement envelope = SerializeValue(result);
            JsonElement value = envelope.GetProperty("value");
            Assert.AreEqual(1, value.GetArrayLength(), "Probe row should have been removed.");

            Assert.IsTrue(envelope.TryGetProperty("after", out JsonElement after),
                "MCP paginated response must carry an 'after' field.");
            Assert.IsFalse(string.IsNullOrEmpty(after.GetString()), "after cursor should be populated.");
            Assert.IsFalse(envelope.TryGetProperty("nextLink", out _),
                "MCP paginated response must NOT carry a 'nextLink' field.");
        }

        /// <summary>
        /// Regression guard for the shape-sentinel removal:
        /// a row whose last column is an array-valued JSON value (e.g. a SQL Server JSON array
        /// or vector/collection column) must be returned verbatim and must NOT be confused
        /// with a pagination marker. Pre-refactor, the in-band sentinel detection in
        /// <see cref="SqlResponseHelpers.OkResponse"/> would have misclassified this shape.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_RowWithArrayColumn_NotMisclassifiedAsPagination()
        {
            // Two rows with array-valued "tags" column. first=5 so HasNext=false.
            JsonElement input = ParseJson(@"[
                { ""id"": 1, ""tags"": [ ""sci-fi"", ""classic"" ] },
                { ""id"": 2, ""tags"": [ ""fantasy"" ] }
            ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id", "tags" },
                first: 5);

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: Mock.Of<ISqlMetadataProvider>(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext());

            JsonElement envelope = SerializeValue(result);
            AssertHasNoPaginationFields(envelope);

            JsonElement value = envelope.GetProperty("value");
            Assert.AreEqual(2, value.GetArrayLength(), "Both rows must be returned, including the array-valued column.");

            // Pin that the array column survived intact and the row count is correct.
            Assert.AreEqual(2, value[0].GetProperty("tags").GetArrayLength());
            Assert.AreEqual("sci-fi", value[0].GetProperty("tags")[0].GetString());
            Assert.AreEqual(1, value[1].GetProperty("tags").GetArrayLength());
            Assert.AreEqual("fantasy", value[1].GetProperty("tags")[0].GetString());
        }

        #endregion

        #region Helpers

        private static JsonElement ParseJson(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        /// <summary>
        /// Serializes the OkObjectResult.Value (an anonymous envelope object) to JSON and
        /// returns it as a JsonElement so individual fields can be asserted.
        /// </summary>
        private static JsonElement SerializeValue(OkObjectResult result)
        {
            string json = JsonSerializer.Serialize(result.Value);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static void AssertHasNoPaginationFields(JsonElement envelope)
        {
            Assert.IsFalse(envelope.TryGetProperty("nextLink", out _), "Envelope unexpectedly contains 'nextLink'.");
            Assert.IsFalse(envelope.TryGetProperty("after", out _), "Envelope unexpectedly contains 'after'.");
        }

        private static FindRequestContext CreateContext(List<string> fieldsToBeReturned, int? first = null)
        {
            SourceDefinition sourceDef = new() { PrimaryKey = new List<string> { "id" } };
            sourceDef.SourceEntityRelationshipMap.Add(ENTITY_NAME, new());
            DatabaseObject dbObject = new DatabaseTable(schemaName: "dbo", tableName: ENTITY_NAME)
            {
                TableDefinition = sourceDef
            };

            FindRequestContext context = new(entityName: ENTITY_NAME, dbo: dbObject, isList: true)
            {
                First = first
            };
            context.FieldsToBeReturned = fieldsToBeReturned;
            return context;
        }

        /// <summary>
        /// Builds a metadata provider that maps any (entity, "id") to the exposed column "id"
        /// so that <see cref="SqlPaginationUtil.MakeCursorFromJsonElement"/> can produce a cursor.
        /// </summary>
        private static ISqlMetadataProvider CreateMetadataProviderWithIdPrimaryKey()
        {
            Mock<ISqlMetadataProvider> mock = new();

            SourceDefinition sourceDef = new() { PrimaryKey = new List<string> { "id" } };
            mock.Setup(m => m.GetSourceDefinition(It.IsAny<string>())).Returns(sourceDef);

            string exposedName = "id";
            mock.Setup(m => m.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out exposedName))
                .Returns(true);

            return mock.Object;
        }

        private static RuntimeConfig CreateRuntimeConfig()
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
                Runtime: new RuntimeOptions(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)));
        }

        #endregion
    }
}
