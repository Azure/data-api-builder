// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for the static request-validation helpers on <see cref="RequestValidator"/>
    /// which validate URL components and request bodies independent of database metadata.
    /// </summary>
    [TestClass]
    public class RequestValidatorStaticUnitTests
    {
        #region ValidateStoredProcedureRequest

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void ValidateStoredProcedureRequest_NoPrimaryKeyRoute_DoesNotThrow(string primaryKeyRoute)
        {
            // Should not throw for empty/whitespace primary key routes.
            RequestValidator.ValidateStoredProcedureRequest(primaryKeyRoute);
        }

        [TestMethod]
        public void ValidateStoredProcedureRequest_WithPrimaryKeyRoute_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidateStoredProcedureRequest("id/1"));

            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.BadRequest, ex.SubStatusCode);
        }

        #endregion

        #region ValidatePrimaryKeyRouteAndQueryStringInURL

        [TestMethod]
        public void ValidatePkRouteAndQueryString_Insert_NoRouteNoQuery_DoesNotThrow()
        {
            RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                EntityActionOperation.Insert, primaryKeyRoute: null, queryString: null);
        }

        [TestMethod]
        public void ValidatePkRouteAndQueryString_Insert_WithPrimaryKeyRoute_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                    EntityActionOperation.Insert, primaryKeyRoute: "id/1", queryString: null));

            Assert.AreEqual(RequestValidator.PRIMARY_KEY_INVALID_USAGE_ERR_MESSAGE, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public void ValidatePkRouteAndQueryString_Insert_WithQueryString_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                    EntityActionOperation.Insert, primaryKeyRoute: null, queryString: "?$filter=id eq 1"));

            Assert.AreEqual(RequestValidator.QUERY_STRING_INVALID_USAGE_ERR_MESSAGE, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [DataTestMethod]
        [DataRow(EntityActionOperation.Delete)]
        [DataRow(EntityActionOperation.Update)]
        [DataRow(EntityActionOperation.UpdateIncremental)]
        [DataRow(EntityActionOperation.Upsert)]
        [DataRow(EntityActionOperation.UpsertIncremental)]
        public void ValidatePkRouteAndQueryString_MutationRequiringKey_MissingRoute_Throws(EntityActionOperation operation)
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                    operation, primaryKeyRoute: null, queryString: null));

            Assert.AreEqual(RequestValidator.PRIMARY_KEY_NOT_PROVIDED_ERR_MESSAGE, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [DataTestMethod]
        [DataRow(EntityActionOperation.Delete)]
        [DataRow(EntityActionOperation.Update)]
        [DataRow(EntityActionOperation.Upsert)]
        public void ValidatePkRouteAndQueryString_MutationRequiringKey_WithRoute_DoesNotThrow(EntityActionOperation operation)
        {
            RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                operation, primaryKeyRoute: "id/1", queryString: null);
        }

        [TestMethod]
        public void ValidatePkRouteAndQueryString_UnsupportedOperation_Throws()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidatePrimaryKeyRouteAndQueryStringInURL(
                    EntityActionOperation.Read, primaryKeyRoute: "id/1", queryString: null));

            Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, ex.SubStatusCode);
        }

        #endregion

        #region ValidateAndParseRequestBody

        [TestMethod]
        public void ValidateAndParseRequestBody_ValidObject_ReturnsObjectElement()
        {
            JsonElement result = RequestValidator.ValidateAndParseRequestBody(@"{""title"":""Hello""}");

            Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
            Assert.AreEqual("Hello", result.GetProperty("title").GetString());
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow(null)]
        public void ValidateAndParseRequestBody_EmptyOrNull_ReturnsDefaultElement(string requestBody)
        {
            JsonElement result = RequestValidator.ValidateAndParseRequestBody(requestBody);

            Assert.AreEqual(JsonValueKind.Undefined, result.ValueKind);
        }

        [TestMethod]
        public void ValidateAndParseRequestBody_JsonArray_ThrowsBatchUnsupported()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidateAndParseRequestBody(@"[{""title"":""A""},{""title"":""B""}]"));

            Assert.AreEqual(RequestValidator.BATCH_MUTATION_UNSUPPORTED_ERR_MESSAGE, ex.Message);
            Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [TestMethod]
        public void ValidateAndParseRequestBody_InvalidJson_ThrowsBadRequest()
        {
            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => RequestValidator.ValidateAndParseRequestBody(@"{""title"":""Hello"));

            Assert.AreEqual(RequestValidator.REQUEST_BODY_INVALID_JSON_ERR_MESSAGE, ex.Message);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.BadRequest, ex.SubStatusCode);
        }

        #endregion
    }
}
