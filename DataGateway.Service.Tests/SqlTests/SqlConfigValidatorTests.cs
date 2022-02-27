using System.Collections.Generic;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Tests.CosmosTests;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass]
    public class SqlConfigValidatorTests
    {
        private static void PerformNonNullFieldTest(string fieldType, ColumnType columnType)
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = $@"
                type Foo {{
                  bar: {fieldType}!
                }}

                type Query {{
                    foo(bar: {fieldType}!): Foo
                }}
                ",
                DatabaseSchema = new DatabaseSchema
                {
                    Tables = new Dictionary<string, TableDefinition> {
                        { "Foo", new TableDefinition {
                            Columns = new Dictionary<string, ColumnDefinition> {
                                { "bar", new ColumnDefinition {
                                    Type = columnType,
                                    IsNullable = false } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, GraphQLType> {
                    { "Foo", new GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        private static void PerformNullableFieldTest(string fieldType, ColumnType columnType)
        {
            IMetadataStoreProvider metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = $@"
                type Foo {{
                  bar: {fieldType}
                }}

                type Query {{
                    foo(bar: {fieldType}): Foo
                }}
                ",
                DatabaseSchema = new DatabaseSchema
                {
                    Tables = new Dictionary<string, TableDefinition> {
                        { "Foo", new TableDefinition {
                            Columns = new Dictionary<string, ColumnDefinition> {
                                { "bar", new ColumnDefinition {
                                    Type = columnType,
                                    IsNullable = true } } },
                            PrimaryKey = new List<string> { "bar" } } } }
                },
                GraphQLTypes = new Dictionary<string, GraphQLType> {
                    { "Foo", new GraphQLType (Table: "Foo", false, "", "") }
                },
                MutationResolvers = null
            };

            GraphQLService graphQLService = new(new Mock<IQueryEngine>().Object, new Mock<IMutationEngine>().Object, metadataStoreProvider);

            SqlConfigValidator validator = new(metadataStoreProvider, graphQLService);

            validator.ValidateConfig();
        }

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        [Ignore("Ignoring test until ID field support is included")]
        public void CanCreateFieldWithIDType() => PerformNonNullFieldTest("ID", ColumnType.Int);

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithStringType() => PerformNonNullFieldTest("String", ColumnType.Varchar);

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithIntType() => PerformNonNullFieldTest("Int", ColumnType.Int);

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithFloatType() => PerformNonNullFieldTest("Float", ColumnType.Float);

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithFloatTypeAndDoubleColumn() => PerformNonNullFieldTest("Float", ColumnType.Double);

        [TestMethod]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithBooleanType() => PerformNonNullFieldTest("Boolean", ColumnType.Bit);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableIDType() => PerformNullableFieldTest("ID", ColumnType.Int);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableStringType() => PerformNullableFieldTest("String", ColumnType.Varchar);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableIntType() => PerformNullableFieldTest("Int", ColumnType.Int);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableFloatType() => PerformNullableFieldTest("Float", ColumnType.Float);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableFloatTypeAndDoubleColumn() => PerformNullableFieldTest("Float", ColumnType.Double);

        [TestMethod]
        [Ignore("Ignored until nullable DB types are supported")]
        [TestCategory("GraphQL to SQL column type validation")]
        public void CanCreateFieldWithNullableBooleanType() => PerformNullableFieldTest("Boolean", ColumnType.Bit);
    }
}
