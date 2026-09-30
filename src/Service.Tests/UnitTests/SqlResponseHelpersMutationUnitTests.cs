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
    /// Unit tests for the mutation-response and primary-key-route helpers on
    /// <see cref="SqlResponseHelpers"/> (the existing SqlResponseHelpersUnitTests covers FormatFindResult).
    /// </summary>
    [TestClass]
    public class SqlResponseHelpersMutationUnitTests
    {
        private const string ENTITY_NAME = "Book";

        #region OkResponse / OkMutationResponse

        [TestMethod]
        public void OkResponse_SingleObject_WrapsInValueArray()
        {
            using JsonDocument doc = JsonDocument.Parse(@"{""id"":1}");
            OkObjectResult result = SqlResponseHelpers.OkResponse(doc.RootElement.Clone());

            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void OkResponse_Array_PreservesElements()
        {
            using JsonDocument doc = JsonDocument.Parse(@"[{""id"":1},{""id"":2}]");
            OkObjectResult result = SqlResponseHelpers.OkResponse(doc.RootElement.Clone());

            Assert.AreEqual(2, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void OkMutationResponse_Dictionary_WrapsSingleRow()
        {
            Dictionary<string, object?> row = new() { ["id"] = 1, ["title"] = "Book" };
            OkObjectResult result = SqlResponseHelpers.OkMutationResponse(row);

            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void OkMutationResponse_JsonObject_WrapsInArray()
        {
            using JsonDocument doc = JsonDocument.Parse(@"{""id"":1}");
            OkObjectResult result = SqlResponseHelpers.OkMutationResponse(doc.RootElement.Clone());

            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void OkMutationResponse_JsonArray_PreservesElements()
        {
            using JsonDocument doc = JsonDocument.Parse(@"[{""id"":1},{""id"":2}]");
            OkObjectResult result = SqlResponseHelpers.OkMutationResponse(doc.RootElement.Clone());

            Assert.AreEqual(2, GetValueArrayLength(result.Value));
        }

        #endregion

        #region ConstructPrimaryKeyRoute

        [TestMethod]
        public void ConstructPrimaryKeyRoute_ViewEntity_ReturnsEmpty()
        {
            DatabaseView view = new(schemaName: "dbo", tableName: "v") { SourceType = EntitySourceType.View, ViewDefinition = new ViewDefinition() };
            FindRequestContext context = new(ENTITY_NAME, view, isList: false);

            string route = SqlResponseHelpers.ConstructPrimaryKeyRoute(context, new Dictionary<string, object?>(), BuildProvider().Object);

            Assert.AreEqual(string.Empty, route);
        }

        [TestMethod]
        public void ConstructPrimaryKeyRoute_TableWithPrimaryKey_ReturnsRoute()
        {
            FindRequestContext context = new(ENTITY_NAME, BuildTableDbo(), isList: false);
            Dictionary<string, object?> row = new() { ["id"] = 1 };

            string route = SqlResponseHelpers.ConstructPrimaryKeyRoute(context, row, BuildProvider().Object);

            Assert.AreEqual("id/1", route);
        }

        [TestMethod]
        public void ConstructPrimaryKeyRoute_MissingExposedName_ReturnsEmpty()
        {
            FindRequestContext context = new(ENTITY_NAME, BuildTableDbo(), isList: false);
            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.GetSourceDefinition(It.IsAny<string>())).Returns(BuildSourceDefinition());
            string discard;
            provider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out discard)).Returns(false);

            string route = SqlResponseHelpers.ConstructPrimaryKeyRoute(context, new Dictionary<string, object?> { ["id"] = 1 }, provider.Object);

            Assert.AreEqual(string.Empty, route);
        }

        [TestMethod]
        public void ConstructPrimaryKeyRoute_PrimaryKeyMissingFromRow_ReturnsEmpty()
        {
            FindRequestContext context = new(ENTITY_NAME, BuildTableDbo(), isList: false);

            string route = SqlResponseHelpers.ConstructPrimaryKeyRoute(context, new Dictionary<string, object?>(), BuildProvider().Object);

            Assert.AreEqual(string.Empty, route);
        }

        #endregion

        #region ConstructOkMutationResponse

        [TestMethod]
        public void ConstructOkMutationResponse_DbPolicyWithDocument_UsesDocument()
        {
            using JsonDocument selectResult = JsonDocument.Parse(@"[{""id"":9}]");
            OkObjectResult result = SqlResponseHelpers.ConstructOkMutationResponse(
                new Dictionary<string, object?>(), selectResult, isReadPermissionConfiguredForRole: true, isDatabasePolicyDefinedForReadAction: true);

            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void ConstructOkMutationResponse_DbPolicyNullDocument_ReturnsEmpty()
        {
            OkObjectResult result = SqlResponseHelpers.ConstructOkMutationResponse(
                new Dictionary<string, object?> { ["id"] = 1 }, jsonDocument: null, isReadPermissionConfiguredForRole: true, isDatabasePolicyDefinedForReadAction: true);

            Assert.AreEqual(0, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void ConstructOkMutationResponse_NoPolicyWithReadPermission_UsesResultRow()
        {
            Dictionary<string, object?> row = new() { ["id"] = 1 };
            OkObjectResult result = SqlResponseHelpers.ConstructOkMutationResponse(
                row, jsonDocument: null, isReadPermissionConfiguredForRole: true, isDatabasePolicyDefinedForReadAction: false);

            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void ConstructOkMutationResponse_NoReadPermission_ReturnsEmpty()
        {
            Dictionary<string, object?> row = new() { ["id"] = 1 };
            OkObjectResult result = SqlResponseHelpers.ConstructOkMutationResponse(
                row, jsonDocument: null, isReadPermissionConfiguredForRole: false, isDatabasePolicyDefinedForReadAction: false);

            Assert.AreEqual(0, GetValueArrayLength(result.Value));
        }

        #endregion

        #region ConstructCreatedResultResponse

        [TestMethod]
        public void ConstructCreatedResultResponse_InsertWithPrimaryKeyRoute_SetsLocation()
        {
            DefaultHttpContext httpContext = new();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("localhost");
            httpContext.Request.Path = "/api/Book";

            CreatedResult result = SqlResponseHelpers.ConstructCreatedResultResponse(
                new Dictionary<string, object?> { ["id"] = 1 },
                jsonDocument: null,
                primaryKeyRoute: "id/1",
                isReadPermissionConfiguredForRole: true,
                isDatabasePolicyDefinedForReadAction: false,
                operationType: EntityActionOperation.Insert,
                baseRoute: string.Empty,
                httpContext: httpContext);

            StringAssert.Contains(result.Location, "id/1");
            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        [TestMethod]
        public void ConstructCreatedResultResponse_UpsertNoRoute_EmptyLocation()
        {
            DefaultHttpContext httpContext = new();

            CreatedResult result = SqlResponseHelpers.ConstructCreatedResultResponse(
                new Dictionary<string, object?> { ["id"] = 1 },
                jsonDocument: null,
                primaryKeyRoute: string.Empty,
                isReadPermissionConfiguredForRole: true,
                isDatabasePolicyDefinedForReadAction: false,
                operationType: EntityActionOperation.Upsert,
                baseRoute: string.Empty,
                httpContext: httpContext);

            Assert.AreEqual(string.Empty, result.Location);
            Assert.AreEqual(1, GetValueArrayLength(result.Value));
        }

        #endregion

        #region Helpers

        private static int GetValueArrayLength(object? resultValue)
        {
            string json = JsonSerializer.Serialize(resultValue);
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("value").GetArrayLength();
        }

        private static SourceDefinition BuildSourceDefinition()
        {
            SourceDefinition sourceDefinition = new() { PrimaryKey = new List<string> { "id" } };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            return sourceDefinition;
        }

        private static DatabaseTable BuildTableDbo() =>
            new(schemaName: "dbo", tableName: "books") { SourceType = EntitySourceType.Table, TableDefinition = BuildSourceDefinition() };

        private static Mock<ISqlMetadataProvider> BuildProvider()
        {
            Mock<ISqlMetadataProvider> provider = new();
            provider.Setup(x => x.GetSourceDefinition(It.IsAny<string>())).Returns(BuildSourceDefinition());
            string exposed = "id";
            provider.Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), "id", out exposed)).Returns(true);
            return provider;
        }

        #endregion
    }
}
