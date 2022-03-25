using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Unit tests for ODataASTVisitor.cs.
    /// testing.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ODataASTVisitorUnitTests
    {
        private static FilterParser _filterParser;
        private const string DEFAULT_ENTITY = "books";

        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            string jsonString = File.ReadAllText("sql-config.json");
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            ResolverConfig? deserializedConfig;
            deserializedConfig = JsonSerializer.Deserialize<ResolverConfig>(jsonString, options);
            _filterParser = new(deserializedConfig.DatabaseSchema);
        }

        #region Positive Tests
        /// <summary>
        /// Verify the correct string is parsed from the
        /// provided filter with eq and null on right side.
        /// </summary>
        [TestMethod]
        public void VisitorLeftFieldRightNullFilterTest()
        {
            PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=id eq null",
                expected: "(id IS NULL)"
                );
        }

        /// <summary>
        /// Verify the correct string is parsed from the
        /// provided filter with ne and null on right side.
        /// </summary>
        [TestMethod]
        public void VisitorLeftFieldRightNotNullFilterTest()
        {
            PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=id ne null",
                expected: "(id IS NOT NULL)"
                );
        }

        /// <summary>
        /// Verify the correct string is parsed from the
        /// provided filter with eq and null on left side.
        /// </summary>
        [TestMethod]
        public void VisitorLeftNullRightFieldFilterTest()
        {
            PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=null eq id",
                expected: "(id IS NULL)"
                );
        }

        /// <summary>
        /// Verify the correct string is parsed from the
        /// provided filter with > and null as value on right side.
        /// </summary>
        [TestMethod]
        public void VisitorLeftFieldGreaterThanRightNullFilterTest()
        {
            PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=id gt null",
                expected: "(id > NULL)"
                );
        }

        #endregion
        #region Negative Tests
        /// <summary>
        /// Verifies that we are not able to parse unsupported Edm Types.
        /// Create a constant node with invalid type and then invoke the visit
        /// function from our visitor using that node.
        /// </summary>
        [TestMethod]
        public void InvalidEdmTypeReferenceTest()
        {

            ConstantNode nodeIn = CreateConstantNode(constantValue: string.Empty, literalText: "text", EdmPrimitiveTypeKind.Geography);
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY);
            Assert.ThrowsException<NotSupportedException>(() => visitor.Visit(nodeIn));
        }

        /// <summary>
        /// Verifies that we throw an exception for values that can
        /// not be parsed into a valid Edm Type Kind. Create a constant
        /// node with a valid type and a value that can not be parsed into
        /// that type and then invoke the visit function from our visitor
        /// using that node.
        /// </summary>
        [TestMethod]
        public void InvalidValueTypeTest()
        {
            ConstantNode nodeIn = CreateConstantNode(constantValue: string.Empty, literalText: "text", EdmPrimitiveTypeKind.Int64);
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY);
            Assert.ThrowsException<ArgumentException>(() => visitor.Visit(nodeIn));
        }

        /// <summary>
        /// Verifies that we throw an exception for binary operations
        /// that are not supported with null types.
        /// </summary>
        [TestMethod]
        public void InvalidBinaryOperatorKindTest()
        {
            ConstantNode constantNode = CreateConstantNode(constantValue: "null", literalText: "null", EdmPrimitiveTypeKind.None, isNull: true);
            BinaryOperatorNode binaryNode = CreateBinaryNode(constantNode, constantNode, BinaryOperatorKind.And);
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY);
            Assert.ThrowsException<NotSupportedException>(() => visitor.Visit(binaryNode));
        }

        /// <summary>
        /// Verifies that we throw an exception for unary operations
        /// that are not supported. Currently only negate is not supported.
        /// </summary>
        [TestMethod]
        public void InvalidUnaryOperatorKindTest()
        {
            ConstantNode constantNode = CreateConstantNode(constantValue: "null", literalText: "null", EdmPrimitiveTypeKind.None, isNull: true);
            UnaryOperatorNode binaryNode = CreateUnaryNode(constantNode, UnaryOperatorKind.Negate);
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY);
            Assert.ThrowsException<ArgumentException>(() => visitor.Visit(binaryNode));
        }

        /// <summary>
        /// Tests that we throw a DataGateway exception for comparison
        /// of a field to a boolean value.
        /// </summary>
        [TestMethod]
        public void InvalidComparisonTypeBoolTest()
        {
            Assert.ThrowsException<DataGatewayException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=id eq (publisher_id gt 1)",
                expected: string.Empty
                ));

        }

        /// <summary>
        /// Tests that we throw an ArgumentException for using an invalid
        /// binary operation.
        /// </summary>
        [TestMethod]
        public void InvalidBinaryOpTest()
        {
            Assert.ThrowsException<ArgumentException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=id eq (publisher_id add 1)",
                expected: string.Empty
                ));

        }

        /// <summary>
        /// Tests that we throw an exception when trying to use an invalid
        /// operator in our filter. In this case an add operator.
        /// </summary>
        [TestMethod]
        public void InvalidNullBinaryOpTest()
        {
            Assert.ThrowsException<DataGatewayException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                filterString: "?$filter=null add 1",
                expected: string.Empty
                ));
        }

        #endregion
        #region Helper Methods

        /// <summary>
        /// Helper function performs the test by creating the Abstract Syntax Tree
        /// and then traversing it with the ODataASTVisitor. We compare the resultant
        /// filter to the expected.
        /// </summary>
        /// <param name="entityName">Represents the entity name in schema.</param>
        /// <param name="filterString">Represents the filter to be applied.</param>
        /// <param name="expected">The expected filter after parsing.</param>
        private static void PerformVisitorTest(
            string entityName,
            string filterString,
            string expected)
        {
            Mock<SqlGraphQLFileMetadataProvider> metaDataStore = new();
            TableDefinition tableDef = new()
            {
                Columns = new()
            };
            metaDataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext context = new(entityName, false);
            Mock<SqlQueryStructure> structure = new(context, metaDataStore.Object);
            FilterClause ast = _filterParser.GetFilterClause(filterString, entityName);
            ODataASTVisitor visitor = new(structure.Object);
            string actual = ast.Expression.Accept<string>(visitor);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Create and return an ODataASTVisitor.
        /// </summary>
        /// <param name="entityName">String represents the entity name.</param>
        /// <param name="isList">bool represents if the context is a list.</param>
        /// <returns></returns>
        private static ODataASTVisitor CreateVisitor(
            string entityName,
            bool isList = false)
        {
            Mock<SqlGraphQLFileMetadataProvider> metaDataStore = new();
            TableDefinition tableDef = new()
            {
                Columns = new()
            };
            metaDataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext context = new(entityName, isList);
            Mock<SqlQueryStructure> structure = new(context, metaDataStore.Object);
            return new ODataASTVisitor(structure.Object);
        }

        /// <summary>
        /// Create and return a Constant Node.
        /// </summary>
        /// <param name="constantValue">The value the node will hold.</param>
        /// <param name="literalText">The literal text of the value.</param>
        /// <param name="edmTypeKind">Represents the type of the value.</param>
        /// <param name="isNull">Represents if the node holds null as its value.</param>
        /// <returns></returns>
        private static ConstantNode CreateConstantNode(
            object constantValue,
            string literalText,
            EdmPrimitiveTypeKind edmTypeKind,
            bool isNull = false)
        {
            IEdmPrimitiveType primitiveType = EdmCoreModel.Instance.GetPrimitiveType(edmTypeKind);
            EdmPrimitiveTypeReference typeRef = null;
            if (!isNull)
            {
                typeRef = new(primitiveType, isNull);
            }

            return new ConstantNode(constantValue, literalText, typeRef);
        }

        /// <summary>
        /// Creates and returns a Binary Node.
        /// </summary>
        /// <param name="left">Represents the left child node.</param>
        /// <param name="right">Represents the right child node.</param>
        /// <param name="op">Represents the binary operation.</param>
        /// <returns></returns>
        private static BinaryOperatorNode CreateBinaryNode(
            SingleValueNode left,
            SingleValueNode right,
            BinaryOperatorKind op)
        {
            return new BinaryOperatorNode(op, left, right);
        }

        /// <summary>
        /// Creates and returns a Unary Node.
        /// </summary>
        /// <param name="child">Represents the child node.</param>
        /// <param name="op">Represents the unary operation.</param>
        /// <returns></returns>
        private static UnaryOperatorNode CreateUnaryNode(
            SingleValueNode child,
            UnaryOperatorKind op)
        {
            return new UnaryOperatorNode(op, child);
        }

        #endregion
    }
}
