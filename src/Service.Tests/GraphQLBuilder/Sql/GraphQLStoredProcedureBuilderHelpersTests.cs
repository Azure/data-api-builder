// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql
{
    /// <summary>
    /// Unit tests for the pure helper methods on <see cref="GraphQLStoredProcedureBuilder"/> that
    /// shape stored-procedure results and default result fields.
    /// </summary>
    [TestClass]
    public class GraphQLStoredProcedureBuilderHelpersTests
    {
        [TestMethod]
        public void FormatStoredProcedureResultAsJsonList_Null_ReturnsEmptyList()
        {
            List<JsonDocument> result = GraphQLStoredProcedureBuilder.FormatStoredProcedureResultAsJsonList(null);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FormatStoredProcedureResultAsJsonList_EmptyArray_ReturnsEmptyList()
        {
            using JsonDocument input = JsonDocument.Parse("[]");
            List<JsonDocument> result = GraphQLStoredProcedureBuilder.FormatStoredProcedureResultAsJsonList(input);

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void FormatStoredProcedureResultAsJsonList_MultipleRows_ReturnsOneDocumentPerRow()
        {
            using JsonDocument input = JsonDocument.Parse(@"[{""id"":1,""title"":""A""},{""id"":2,""title"":""B""}]");
            List<JsonDocument> result = GraphQLStoredProcedureBuilder.FormatStoredProcedureResultAsJsonList(input);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(1, result[0].RootElement.GetProperty("id").GetInt32());
            Assert.AreEqual("B", result[1].RootElement.GetProperty("title").GetString());
        }

        [TestMethod]
        public void GetDefaultResultFieldForStoredProcedure_ReturnsResultStringField()
        {
            FieldDefinitionNode field = GraphQLStoredProcedureBuilder.GetDefaultResultFieldForStoredProcedure();

            Assert.AreEqual("result", field.Name.Value);
            Assert.AreEqual(0, field.Arguments.Count);
            Assert.IsInstanceOfType(field.Type, typeof(NamedTypeNode));
            Assert.AreEqual("String", ((NamedTypeNode)field.Type).Name.Value);
        }
    }
}
