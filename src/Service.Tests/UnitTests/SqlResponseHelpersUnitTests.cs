// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.Json;
using System.Web;
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
    /// Tests 1-5 pin the response envelope shape across each path the method takes:
    /// (1) empty result, (2) single object, (3) collection without next page,
    /// (4) collection with next page (REST), (5) collection with next page (MCP).
    ///
    /// Test 6 documents that rows containing array-valued columns (the shape enabled by
    /// SQL Server's JSON/vector types) round-trip through the response pipeline unchanged
    /// on the no-pagination path. Test 7 extends that coverage to the paginated path,
    /// confirming the <c>nextLink</c> envelope is still produced when array-typed columns
    /// are present.
    ///
    /// Test 8 is the load-bearing regression guard for the shape-sentinel removal:
    /// it pins that a result whose last top-level element is itself a JSON array — the
    /// exact trigger the pre-refactor <see cref="SqlResponseHelpers.OkResponse"/> used to
    /// detect a pagination sentinel — is now correctly treated as ordinary data.
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
        /// Pins that rows containing array-valued columns (e.g. a SQL Server JSON array, vector,
        /// or other collection-typed column) round-trip through the response pipeline unchanged.
        /// This is forward-looking coverage for query shapes enabled by SQL Server's JSON/vector
        /// types: the array values live inside object-shaped rows, so this case did not actually
        /// trigger the pre-refactor shape sentinel — but it documents the supported shape and
        /// guards against future regressions in extra-field stripping or envelope construction.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_RowWithArrayColumn_RoundTripsUnchanged()
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

        /// <summary>
        /// Pagination + array-valued columns: rows that contain array-typed columns (e.g. SQL Server
        /// JSON arrays, vector/collection types) must still produce a correctly paginated envelope
        /// when <c>$first</c> is exceeded. Pins that (a) the +1 probe row is trimmed, (b) the
        /// <c>nextLink</c> field is emitted on the REST path, and (c) the surviving row's array
        /// column round-trips intact. Coverage gap surfaced in PR review (companion to
        /// <see cref="FormatFindResult_RowWithArrayColumn_RoundTripsUnchanged"/>, which only exercises
        /// the no-pagination path).
        /// </summary>
        [TestMethod]
        public void FormatFindResult_RowWithArrayColumn_WithNextPage_Rest_ReturnsNextLinkEnvelope()
        {
            // first = 1, 2 rows: HasNext is true, last (probe) row is dropped.
            JsonElement input = ParseJson(@"[
                { ""id"": 1, ""tags"": [ ""sci-fi"", ""classic"" ] },
                { ""id"": 2, ""tags"": [ ""fantasy"" ] }
            ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id", "tags" },
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

            // Array-typed column on the surviving row must round-trip unchanged.
            JsonElement tags = value[0].GetProperty("tags");
            Assert.AreEqual(JsonValueKind.Array, tags.ValueKind, "Array-typed column must remain an array.");
            Assert.AreEqual(2, tags.GetArrayLength());
            Assert.AreEqual("sci-fi", tags[0].GetString());
            Assert.AreEqual("classic", tags[1].GetString());

            Assert.IsTrue(envelope.TryGetProperty("nextLink", out JsonElement nextLink),
                "REST paginated response with array-valued columns must still carry a 'nextLink' field.");
            Assert.IsTrue(nextLink.GetString()!.Contains("$after="), "nextLink should encode the $after cursor.");
            Assert.IsFalse(envelope.TryGetProperty("after", out _),
                "REST paginated response must NOT carry an 'after' field.");

            // Round-trip the cursor: feed the $after token from the page-1 nextLink into a second
            // FormatFindResult call simulating the next-page database response. With only the
            // remaining row left, the envelope must contain { id: 2, tags: [fantasy] } and no
            // further pagination metadata.
            NameValueCollection afterQuery = HttpUtility.ParseQueryString(new Uri(nextLink.GetString()!).Query);
            Assert.IsFalse(string.IsNullOrEmpty(afterQuery["$after"]),
                "$after cursor must be present in nextLink query string.");

            JsonElement nextPageInput = ParseJson(@"[ { ""id"": 2, ""tags"": [ ""fantasy"" ] } ]");
            FindRequestContext nextPageContext = CreateContext(
                fieldsToBeReturned: new List<string> { "id", "tags" },
                first: 1);
            nextPageContext.ParsedQueryString = afterQuery;

            OkObjectResult nextPageResult = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: nextPageInput,
                context: nextPageContext,
                sqlMetadataProvider: CreateMetadataProviderWithIdPrimaryKey(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: httpContext);

            JsonElement nextPageEnvelope = SerializeValue(nextPageResult);
            AssertHasNoPaginationFields(nextPageEnvelope);

            JsonElement nextPageValue = nextPageEnvelope.GetProperty("value");
            Assert.AreEqual(1, nextPageValue.GetArrayLength(), "Page 2 must contain exactly one row.");
            Assert.AreEqual(2, nextPageValue[0].GetProperty("id").GetInt32());

            JsonElement nextPageTags = nextPageValue[0].GetProperty("tags");
            Assert.AreEqual(JsonValueKind.Array, nextPageTags.ValueKind, "Array-typed column must remain an array on page 2.");
            Assert.AreEqual(1, nextPageTags.GetArrayLength());
            Assert.AreEqual("fantasy", nextPageTags[0].GetString());
        }

        /// <summary>
        /// Regression guard for the actual shape-sentinel failure mode: when the result list's
        /// last top-level element is itself a JSON array (a non-object row, as could be produced
        /// by future query shapes that project array-typed values at the row level), the response
        /// must be returned verbatim under <c>value</c>. Pre-refactor, <see cref="SqlResponseHelpers.OkResponse"/>
        /// inspected <c>JsonValueKind.Array</c> on the last element and would have attempted to
        /// unpack it as a <c>{ "nextLink" }</c> / <c>{ "after" }</c> sentinel, producing an
        /// incorrect envelope. With shape-based detection removed, the array element is now
        /// correctly treated as ordinary data.
        /// </summary>
        [TestMethod]
        public void FormatFindResult_TopLevelArrayTailRow_IsNotMisclassifiedAsPaginationSentinel()
        {
            // Last top-level element is a JSON array — the exact shape the old in-band sentinel
            // detection used as its trigger. first=10 keeps HasNext=false so the no-pagination
            // path is taken; without the refactor, OkResponse would have misfired here.
            JsonElement input = ParseJson(@"[
                { ""id"": 1 },
                { ""id"": 2 },
                [ 1, 2, 3 ]
            ]");
            FindRequestContext context = CreateContext(
                fieldsToBeReturned: new List<string> { "id" },
                first: 10);

            OkObjectResult result = SqlResponseHelpers.FormatFindResult(
                findOperationResponse: input,
                context: context,
                sqlMetadataProvider: Mock.Of<ISqlMetadataProvider>(),
                runtimeConfig: CreateRuntimeConfig(),
                httpContext: new DefaultHttpContext());

            JsonElement envelope = SerializeValue(result);
            AssertHasNoPaginationFields(envelope);

            JsonElement value = envelope.GetProperty("value");
            Assert.AreEqual(3, value.GetArrayLength(),
                "All three top-level elements must be preserved; the trailing array must NOT be unpacked as a pagination sentinel.");
            Assert.AreEqual(JsonValueKind.Object, value[0].ValueKind);
            Assert.AreEqual(JsonValueKind.Object, value[1].ValueKind);
            Assert.AreEqual(JsonValueKind.Array, value[2].ValueKind);
            Assert.AreEqual(3, value[2].GetArrayLength());
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
                First = first,
                FieldsToBeReturned = fieldsToBeReturned
            };
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
