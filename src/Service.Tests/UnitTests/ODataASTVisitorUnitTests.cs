// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Parsers;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.OData;
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
        private const string DEFAULT_ENTITY = "SupportedType";
        private const string DEFAULT_SCHEMA_NAME = "dbo";
        private const string DEFAULT_TABLE_NAME = "type_table";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Positive Tests
        /// <summary>
        /// Verify the correct string is parsed from the
        /// provided filter with eq and null on right side.
        /// </summary>
        [DataRow("typeid eq 2147483647", "([id] = @param1)", DisplayName = "Equate int types.")]
        [DataRow("typeid eq null", "([id] IS NULL)", DisplayName = "Equate int to null.")]
        [DataRow("byte_types eq 127", "([byte_types] = @param1)", DisplayName = "Equate byte types.")]
        [DataRow("short_types eq 255", "([short_types] = @param1)", DisplayName = "Equate short types.")]
        [DataRow("long_types eq 9223372036854775807", "([long_types] = @param1)", DisplayName = "Equate long types.")]
        [DataRow("string_types eq 'hello'", "([string_types] = @param1)", DisplayName = "Equate string types.")]
        [DataRow("nvarchar_string_types eq 'hello'", "([nvarchar_string_types] = @param1)", DisplayName = "Equate nvarchar string types.")]
        [DataRow("single_types eq 10.0", "([single_types] = @param1)", DisplayName = "Equate single types.")]
        [DataRow("float_types eq 65535.9", "([float_types] = @param1)", DisplayName = "Equate float types.")]
        [DataRow("decimal_types eq 25.5", "([decimal_types] = @param1)", DisplayName = "Equate decimal types.")]
        [DataRow("boolean_types eq true", "([boolean_types] = @param1)", DisplayName = "Equate boolean types.")]
        [DataRow("date_types eq 9999-12-31", "([date_types] = @param1)",
            DisplayName = "Equate date types.")]
        [DataRow("datetime_types eq  2023-01-24T12:51:59Z", "([datetime_types] = @param1)",
            DisplayName = "Equate datetime types.")]
        [DataRow("datetime2_types eq  9998-12-31T21:59:59.99999Z", "([datetime2_types] = @param1)",
            DisplayName = "Equate datetime2 types.")]
        [DataRow("datetimeoffset_types eq 9998-12-31T21:59:59.99999-14:00",
            "([datetimeoffset_types] = @param1)", DisplayName = "Equate datetimeoffset types.")]
        [DataRow("smalldatetime_types eq 2079-06-06", "([smalldatetime_types] = @param1)",
            DisplayName = "Equate smalldatetime types.")]
        [DataRow("bytearray_types eq 1000", "([bytearray_types] = @param1)", DisplayName = "Equate bytearray types.")]
        [DataRow("uuid_types eq 9A19103F-16F7-4668-BE54-9A1E7A4F7556", "([uuid_types] = @param1)", DisplayName = "Equate uuid(guid) types.")]
        [DataRow("time_types eq 10:23:54.9999999", "([time_types] = @param1)", DisplayName = "Equate time types.")]
        [DataRow("time_types eq null", "([time_types] IS NULL)", DisplayName = "Equate time types for null.")]
        [TestMethod]
        public void VisitorLeftFieldRightConstantFilterTest(string filterExp, string expectedPredicate)
        {
            PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: $"?$filter={filterExp}",
                expected: expectedPredicate
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
                filterString: "?$filter=typeid ne null",
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
                filterString: "?$filter=null eq typeid",
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
                filterString: "?$filter=typeid gt null",
                expected: "([id] > NULL)"
                );
        }

        /// <summary>
        /// Tests processed authorization policies (@claims.claimName eq @item.columnName) -> ('UserName' eq ScreenName)
        /// against the custom OData Filter parser resolver ClaimsTypeDataUriResolver.
        /// The columns xyz_types are sourced from type_table.
        /// Constant values/literals in expressions are parsed by Microsoft.OData.UriParser.ExpressionLexer which
        /// attempts to resolve value to its OData(EdmPrimitiveTypeKind) via
        /// https://github.com/OData/odata.net/blob/f3bf65a74a7ed4028ff8074ccae31e4c2019772d/src/Microsoft.OData.Core/UriParser/ExpressionLexer.cs#L1206-L1221
        /// </summary>
        /// <param name="resolvedAuthZPolicyText">Filter parser input, the processed authorization policy</param>
        /// <param name="errorExpected">Whether an OData Filter parser error is expected</param>
        /// <seealso cref="https://learn.microsoft.com/dotnet/framework/data/adonet/sql/linq/sql-clr-type-mapping"/>
        [DataTestMethod]
        // Constant on left side and OData EDM object on right side of binary operator. (L->R)
        [DataRow("'1' eq int_types", false, DisplayName = "L->R: Cast token claim of type string to integer")]
        [DataRow("12.24 eq float_types", false, DisplayName = "L->R: Cast token claim of type single to type double (CLR) which maps to (SQL) float")]
        [DataRow("'13B4F4EC-C45B-46EC-99F2-77BC22A256A7' eq uuid_types", false, DisplayName = "L->R: Cast token claim of type string to GUID")]
        [DataRow("'true' eq boolean_types", false, DisplayName = "L->R: Cast token claim of type string to bool (true)")]
        [DataRow("'false' eq boolean_types", false, DisplayName = "L->R: Cast token claim of type string to bool (false)")]
        [DataRow("1 eq string_types", false, DisplayName = "L->R: Cast token claim of type int to string")]
        [DataRow("true eq string_types", false, DisplayName = "L->R: Cast token claim of type bool to string")]
        // Constant on right side and OData EDM object on left side of binary operator. (R->L)
        [DataRow("int_types eq '1'", false, DisplayName = "R->L: Cast token claim of type string to integer")]
        [DataRow("float_types eq 12.24", false, DisplayName = "R->L: Cast token claim of type single to type double (CLR) which maps to (SQL) float")]
        [DataRow("uuid_types eq '13B4F4EC-C45B-46EC-99F2-77BC22A256A7'", false, DisplayName = "R->L: Cast token claim of type string to GUID")]
        [DataRow("boolean_types eq 'true'", false, DisplayName = "R->L: Cast token claim of type string to bool (true)")]
        [DataRow("boolean_types eq 'false'", false, DisplayName = "R->L: Cast token claim of type string to bool (false)")]
        [DataRow("string_types eq 1", false, DisplayName = "R->L: Cast token claim of type int to string")]
        [DataRow("string_types eq true", false, DisplayName = "R->L: Cast token claim of type bool to string")]
        // Comparisons expected to fail due to inability to cast
        [DataRow("boolean_types eq 2", true, DisplayName = "Fail to cast arbitrary int to bool")]
        [DataRow("uuid_types eq 1", true, DisplayName = "Fail to cast arbitrary int to GUID")]
        [DataRow("uuid_types eq 'stringConstant'", true, DisplayName = "Fail to cast arbitrary string to GUID")]
        public void CustomODataUriParserResolverTest(string resolvedAuthZPolicyText, bool errorExpected)
        {
            // Arrange
            string entityName = "SupportedType";
            string tableName = "type_table";
            string filterQueryString = "?$filter=" + resolvedAuthZPolicyText;
            string expectedErrorMessageFragment = "A binary operator with incompatible types was detected.";

            //Act + Assert
            try
            {
                FilterClause ast = _sqlMetadataProvider
                    .GetODataParser()
                    .GetFilterClause(
                        filterQueryString,
                        resourcePath: $"{entityName}.{DEFAULT_SCHEMA_NAME}.{tableName}",
                        customResolver: new ClaimsTypeDataUriResolver());
                Assert.IsFalse(errorExpected, message: "Filter clause creation was expected to fail.");
            }
            catch (Exception e) when (e is DataApiBuilderException || e is ODataException)
            {
                Assert.IsTrue(errorExpected, message: "Filter clause creation was not expected to fail.");
                Assert.IsTrue(e.Message.Contains(expectedErrorMessageFragment), message: e.Message);
            }
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
        /// Tests that we throw an exception when trying to use an invalid
        /// Time with negative value or time > 24 hours.
        /// </summary>
        [DataTestMethod]
        [DataRow("time_types eq 25:23:54.9999999", DisplayName = "Exception thrown with invalid time>24 hrs.")]
        [DataRow("time_types eq -13:23:54.9999999", DisplayName = "Exception thrown with invalid time>24 hrs.")]
        public void InvalidTimeTypeODataFilterTest(string filterExp)
        {
            Assert.ThrowsException<DataApiBuilderException>(() => PerformVisitorTest(
                entityName: DEFAULT_ENTITY,
                schemaName: DEFAULT_SCHEMA_NAME,
                tableName: DEFAULT_TABLE_NAME,
                filterString: $"?$filter={filterExp}",
                expected: string.Empty
                ));
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
                filterString: "?$filter=typeid eq (typeid gt 1)",
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
                filterString: "?$filter=typeid eq (typeid add 1)",
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
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            AuthorizationResolver authorizationResolver = new(
                runtimeConfigProvider,
                _metadataProviderFactory.Object);
            Mock<SqlQueryStructure> structure = new(
                context,
                _sqlMetadataProvider,
                authorizationResolver,
                runtimeConfigProvider,
                new GQLFilterParser(runtimeConfigProvider, _metadataProviderFactory.Object),
                null) // setting httpContext as null for the tests.
            { CallBase = true }; // setting CallBase = true enables calling the actual method on the mocked object without needing to mock the method behavior.
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
