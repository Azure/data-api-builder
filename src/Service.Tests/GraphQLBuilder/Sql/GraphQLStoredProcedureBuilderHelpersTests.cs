// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Service.Exceptions;
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

        /// <summary>
        /// Verifies configured parameter text is converted into the GraphQL scalar name and value node for every supported CLR type.
        /// </summary>
        [DataTestMethod]
        [DataRow(typeof(Guid), "d2719f98-e062-4ae8-a786-4ea9c3524d7c", "UUID")]
        [DataRow(typeof(byte), "255", "UnsignedByte")]
        [DataRow(typeof(short), "-32768", "Short")]
        [DataRow(typeof(int), "-2147483648", "Int")]
        [DataRow(typeof(long), "9223372036854775807", "Long")]
        [DataRow(typeof(float), "1.25", "Single")]
        [DataRow(typeof(double), "2.5", "Float")]
        [DataRow(typeof(decimal), "3.75", "Decimal")]
        [DataRow(typeof(string), "text", "String")]
        [DataRow(typeof(bool), "true", "Boolean")]
        [DataRow(typeof(DateTime), "2025-01-02T03:04:05Z", "DateTime")]
        [DataRow(typeof(byte[]), "AQID", "Base64String")]
        [DataRow(typeof(TimeOnly), "12:34:56", "LocalTime")]
        public void ConvertValueToGraphQLType_ConvertsEverySupportedScalar(
            Type systemType,
            string configuredValue,
            string expectedGraphQLType)
        {
            Tuple<string, IValueNode> result = InvokeConvertValueToGraphQLType(configuredValue, systemType);

            Assert.AreEqual(expectedGraphQLType, result.Item1);
            Assert.IsNotNull(result.Item2);
        }

        [DataTestMethod]
        [DataRow("1", true)]
        [DataRow("0", false)]
        [DataRow("TrUe", true)]
        [DataRow("FaLsE", false)]
        public void ConvertValueToGraphQLType_ConvertsSupportedBooleanRepresentations(string configuredValue, bool expected)
        {
            Tuple<string, IValueNode> result = InvokeConvertValueToGraphQLType(configuredValue, typeof(bool));

            Assert.AreEqual(expected, ((BooleanValueNode)result.Item2).Value);
        }

        [TestMethod]
        public void ConvertValueToGraphQLType_InvalidValueWrapsConversionFailure()
        {
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(
                () => InvokeConvertValueToGraphQLType("not-a-boolean", typeof(bool)));

            Assert.IsInstanceOfType<DataApiBuilderException>(exception.InnerException);
        }

        private static Tuple<string, IValueNode> InvokeConvertValueToGraphQLType(string configuredValue, Type systemType)
        {
            MethodInfo method = typeof(GraphQLStoredProcedureBuilder).GetMethod(
                "ConvertValueToGraphQLType",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            ParameterDefinition parameter = new() { SystemType = systemType };

            return (Tuple<string, IValueNode>)method.Invoke(null, new object[] { configuredValue, parameter })!;
        }
    }
}
