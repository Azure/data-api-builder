// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Reflection;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosODataVisitorHelperTests
    {
        [DataTestMethod]
        [DataRow(BinaryOperatorKind.Equal, "=")]
        [DataRow(BinaryOperatorKind.GreaterThan, ">")]
        [DataRow(BinaryOperatorKind.GreaterThanOrEqual, ">=")]
        [DataRow(BinaryOperatorKind.LessThan, "<")]
        [DataRow(BinaryOperatorKind.LessThanOrEqual, "<=")]
        [DataRow(BinaryOperatorKind.NotEqual, "!=")]
        [DataRow(BinaryOperatorKind.And, "AND")]
        [DataRow(BinaryOperatorKind.Or, "OR")]
        public void BinaryOperatorMapping_ReturnsExpectedOperator(BinaryOperatorKind operation, string expected)
        {
            Assert.AreEqual(expected, InvokeStatic("GetFilterPredicateOperator", operation));
        }

        [TestMethod]
        public void BinaryOperatorMapping_UnknownOperationThrows()
        {
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeStatic("GetFilterPredicateOperator", (BinaryOperatorKind)int.MaxValue));

            Assert.IsInstanceOfType<ArgumentException>(exception.InnerException);
        }

        [TestMethod]
        public void UnaryOperatorMapping_HandlesNotAndRejectsUnknownOperation()
        {
            Assert.AreEqual("NOT", InvokeStatic("GetFilterPredicateOperator", UnaryOperatorKind.Not));
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeStatic("GetFilterPredicateOperator", (UnaryOperatorKind)int.MaxValue));
            Assert.IsInstanceOfType<ArgumentException>(exception.InnerException);
        }

        [DataTestMethod]
        [DataRow(BinaryOperatorKind.Equal, "field", "NULL", "(field IS NULL)")]
        [DataRow(BinaryOperatorKind.Equal, "NULL", "field", "(field IS NULL)")]
        [DataRow(BinaryOperatorKind.NotEqual, "field", "NULL", "(field IS NOT NULL)")]
        [DataRow(BinaryOperatorKind.NotEqual, "NULL", "field", "(field IS NOT NULL)")]
        [DataRow(BinaryOperatorKind.GreaterThan, "field", "NULL", "(field > NULL)")]
        [DataRow(BinaryOperatorKind.GreaterThanOrEqual, "field", "NULL", "(field >= NULL)")]
        [DataRow(BinaryOperatorKind.LessThan, "field", "NULL", "(field < NULL)")]
        [DataRow(BinaryOperatorKind.LessThanOrEqual, "field", "NULL", "(field <= NULL)")]
        public void CreateNullResult_FormatsSupportedOperations(
            BinaryOperatorKind operation,
            string left,
            string right,
            string expected)
        {
            Assert.AreEqual(expected, InvokeStatic("CreateNullResult", operation, left, right));
        }

        [TestMethod]
        public void CreateNullResult_RejectsUnsupportedOperation()
        {
            TargetInvocationException exception = Assert.ThrowsException<TargetInvocationException>(() =>
                InvokeStatic("CreateNullResult", BinaryOperatorKind.And, "field", "NULL"));

            Assert.IsInstanceOfType<NotSupportedException>(exception.InnerException);
        }

        [TestMethod]
        public void Visitor_HandlesNullBinaryUnaryConvertAndTypedConstants()
        {
            TestQueryStructure structure = new();
            ODataASTCosmosVisitor visitor = new("c", structure);
            ConstantNode nullNode = CreateConstantNode(null!, "null", EdmPrimitiveTypeKind.String, isNull: true);
            ConstantNode valueNode = CreateConstantNode(7, "7", EdmPrimitiveTypeKind.Int32);
            BinaryOperatorNode binary = new(BinaryOperatorKind.Equal, valueNode, nullNode);
            UnaryOperatorNode unary = new(UnaryOperatorKind.Not, CreateConstantNode(true, "true", EdmPrimitiveTypeKind.Boolean));
            EdmPrimitiveTypeReference intType = new(EdmCoreModel.Instance.GetPrimitiveType(EdmPrimitiveTypeKind.Int32), false);
            ConvertNode convert = new(valueNode, intType);

            Assert.AreEqual("(@param0 IS NULL)", binary.Accept(visitor));
            Assert.AreEqual("(NOT @param1 )", unary.Accept(visitor));
            Assert.AreEqual("@param2", convert.Accept(visitor));
            Assert.AreEqual("NULL", nullNode.Accept(visitor));
            CollectionAssert.AreEqual(new object[] { 7, true, 7 },
                new System.Collections.Generic.List<object>(System.Linq.Enumerable.Select(structure.Parameters.Values, p => p.Value)));
        }

        private static ConstantNode CreateConstantNode(
            object value,
            string literal,
            EdmPrimitiveTypeKind kind,
            bool isNull = false)
        {
            EdmPrimitiveTypeReference? type = isNull
                ? null
                : new EdmPrimitiveTypeReference(EdmCoreModel.Instance.GetPrimitiveType(kind), false);
            return new ConstantNode(value, literal, type);
        }

        private static string InvokeStatic(string methodName, params object[] arguments)
        {
            Type[] parameterTypes = Array.ConvertAll(arguments, argument => argument.GetType());
            MethodInfo method = typeof(ODataASTCosmosVisitor).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                parameterTypes,
                modifiers: null)!;
            return (string)method.Invoke(null, arguments)!;
        }

        private sealed class TestQueryStructure : BaseQueryStructure
        {
            public TestQueryStructure()
                : base(
                    Mock.Of<ISqlMetadataProvider>(),
                    Mock.Of<IAuthorizationResolver>(),
                    gQLFilterParser: null!)
            {
            }
        }
    }
}
