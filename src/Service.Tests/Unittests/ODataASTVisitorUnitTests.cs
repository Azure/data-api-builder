using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for ODataASTVisitor.cs.
    /// testing.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ODataASTVisitorUnitTests : SqlTestBase
    {
        private const string DEFAULT_ENTITY = "Book";
        private const string DEFAULT_SCHEMA_NAME = "dbo";
        private const string DEFAULT_TABLE_NAME = "books";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
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
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: "?$filter=id eq null",
                expected: "([id] IS NULL)"
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
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: "?$filter=id ne null",
                expected: "([id] IS NOT NULL)"
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
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: "?$filter=null eq id",
                expected: "([id] IS NULL)"
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
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: "?$filter=id gt null",
                expected: "([id] > NULL)"
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
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY, DEFAULT_SCHEMA_NAME, DEFAULT_TABLE_NAME);
            Assert.ThrowsException<NotSupportedException>(() => visitor.Visit(nodeIn));
        }

        /// <summary>
        /// Verifies that we throw an exception for values that can
        /// not be parsed into a valid Edm Type Kind. Create a constant
        /// node with a valid type and a value that cannot be parsed into
        /// that type and then invoke the visit function from our visitor
        /// using that node.
        /// </summary>
        [TestMethod]
        public void InvalidValueTypeTest()
        {
            ConstantNode nodeIn = CreateConstantNode(constantValue: string.Empty, literalText: "text", EdmPrimitiveTypeKind.Int32);
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY, DEFAULT_SCHEMA_NAME, DEFAULT_TABLE_NAME);
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
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY, DEFAULT_SCHEMA_NAME, DEFAULT_TABLE_NAME);
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
            ODataASTVisitor visitor = CreateVisitor(DEFAULT_ENTITY, DEFAULT_SCHEMA_NAME, DEFAULT_TABLE_NAME);
            Assert.ThrowsException<ArgumentException>(() => visitor.Visit(binaryNode));
        }

        /// <summary>
        /// Tests that we throw a DataApiBuilder exception for comparison
        /// of a field to a boolean value.
        /// </summary>
        [TestMethod]
        public void InvalidComparisonTypeBoolTest()
        {
            Assert.ThrowsException<DataApiBuilderException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
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
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
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
            Assert.ThrowsException<DataApiBuilderException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
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
            string schemaName,
            string tableName,
            string filterString,
            string expected)
        {
            FilterClause ast = _sqlMetadataProvider.GetODataParser().
                GetFilterClause(filterString, $"{entityName}.{schemaName}.{tableName}");
            ODataASTVisitor visitor = CreateVisitor(entityName, schemaName, tableName);
            string actual = ast.Expression.Accept(visitor);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// Create and return an ODataASTVisitor.
        /// </summary>
        /// <param name="entityName">String represents the entity name.</param>
        /// <param name="schemaName">String represents the schema of the source entity.</param>
        /// <param name="tableName">String represents the table name of the source entity.</param>
        /// <param name="isList">bool represents if the context is a list.</param>
        /// <returns></returns>
        private static ODataASTVisitor CreateVisitor(
            string entityName,
            string schemaName,
            string tableName,
            bool isList = false)
        {
            DatabaseObject dbo = new DatabaseTable()
            {
                SchemaName = schemaName,
                Name = tableName
            };
            FindRequestContext context = new(entityName, dbo, isList);

            // Null out the baseTableForRequestDefinition.
            Mock<SqlQueryStructure> structure = new(context, _sqlMetadataProvider, _runtimeConfigProvider, null);
            return new ODataASTVisitor(structure.Object, _sqlMetadataProvider);
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
