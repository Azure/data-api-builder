using System.Net;
using System.Text.Json;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit Tests for targetting code paths in Request
    /// Context classes that are not easily tested through
    /// integration testing.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RequestContextUnitTests
    {
        /// <summary>
        /// Verify that if a payload does not Deserialize
        /// when constructing an InsertRequestContext that the
        /// correct exception is thrown and validate the message
        /// and status codes.
        /// </summary>
        [TestMethod]
        public void ExceptionOnInsertPayloadFailDeserialization()
        {
            // "null" will be instantiated as a string of "null", which will
            // deserialize into type of null, this should throw excception
            JsonElement payload = JsonSerializer.Deserialize<JsonElement>("\"null\"");
            OperationAuthorizationRequirement verb = new();
            try
            {
                InsertRequestContext context = new(entityName: string.Empty, insertPayloadRoot: payload, httpVerb: verb, operationType: Operation.Insert);
                Assert.Fail();
            }
            catch (DataGatewayException e)
            {
                Assert.AreEqual("The request body is not in a valid JSON format.", e.Message);
                Assert.AreEqual(HttpStatusCode.BadRequest, e.StatusCode);
                Assert.AreEqual(DataGatewayException.SubStatusCodes.BadRequest, e.SubStatusCode);
            }
        }

        /// <summary>
        /// Verify that if a payload deserializes to
        /// the empty string, we instantiate an
        /// empty FieldsValuePairsInBody
        /// </summary>
        [TestMethod]
        public void EmptyInsertPayloadTest()
        {
            // null will be instantiated as the empty string which will
            // mean an empty FieldValuePairsInBody
            JsonElement payload = JsonSerializer.Deserialize<JsonElement>("null");
            OperationAuthorizationRequirement verb = new();
            InsertRequestContext context = new(entityName: string.Empty, insertPayloadRoot: payload, httpVerb: verb, operationType: Operation.Insert);
            Assert.AreEqual(0, context.FieldValuePairsInBody.Count);
        }
    }
}
