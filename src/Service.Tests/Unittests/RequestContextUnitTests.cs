// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit Tests for targeting code paths in Request
    /// Context classes that are not easily tested through
    /// integration testing.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RequestContextUnitTests
    {
        private static DatabaseObject _defaultDbObject = new DatabaseTable()
        {
            SchemaName = string.Empty,
            Name = string.Empty,
            TableDefinition = new()
        };

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
            try
            {
                InsertRequestContext context = new(entityName: string.Empty,
                                                    dbo: _defaultDbObject,
                                                    insertPayloadRoot: payload,
                                                    operationType: EntityActionOperation.Insert);
                Assert.Fail();
            }
            catch (DataApiBuilderException e)
            {
                Assert.AreEqual("The request body is not in a valid JSON format.", e.Message);
                Assert.AreEqual(HttpStatusCode.BadRequest, e.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.BadRequest, e.SubStatusCode);
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
            InsertRequestContext context = new(entityName: string.Empty,
                                                dbo: _defaultDbObject,
                                                insertPayloadRoot: payload,
                                                operationType: EntityActionOperation.Insert);
            Assert.AreEqual(0, context.FieldValuePairsInBody.Count);
        }
    }
}
