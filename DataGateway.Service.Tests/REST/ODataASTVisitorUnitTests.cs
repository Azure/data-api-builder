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
        /// provided filter with eq and null as value on right side.
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
        /// provided filter with ne and null as value on right side.
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
            Mock<IMetadataStoreProvider> metaDataStore = new();
            TableDefinition tableDef = new()
            {
                Columns = new()
            };
            metaDataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(tableDef);
            FindRequestContext context = new(DEFAULT_ENTITY, false);
            Mock<SqlQueryStructure> structure = new(context, metaDataStore.Object);
            IEdmPrimitiveType primitiveType = EdmCoreModel.Instance.GetPrimitiveType(EdmPrimitiveTypeKind.Geography);
            EdmPrimitiveTypeReference typeRef = new(primitiveType, false);
            ConstantNode nodeIn = new(string.Empty, "not empty", typeRef);
            ODataASTVisitor visitor = new(structure.Object);
            Assert.ThrowsException<NotSupportedException>(() => visitor.Visit(nodeIn));
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
        /// operator in our filter. In those case an add operator.
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
            Mock<IMetadataStoreProvider> metaDataStore = new();
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
        #endregion
    }
}
