// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql
{
    /// <summary>
    /// Integration tests that verify stored-procedure parameter descriptions flow
    /// end-to-end through the full production pipeline:
    ///   config parameters.description
    ///     → SqlMetadataProvider.FillSchemaForStoredProcedureAsync (merges onto ParameterDefinition)
    ///     → GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema (reads description)
    ///     → GraphQL argument description
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class StoredProcedureBuilderDescriptionMsSqlIntegrationTests : SqlTestBase
    {
        private static RuntimeConfig _baseConfig;

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
            _baseConfig = SqlTestHelper.SetupRuntimeConfig();
        }

        /// <summary>
        /// Verifies that a description configured on a stored-procedure parameter in the
        /// runtime config is propagated through the SQL metadata provider and reflected in
        /// the generated GraphQL argument description.
        ///
        /// Uses the existing <c>get_book_by_id</c> stored procedure (defined in the MsSql
        /// test schema) with a config-side description override on its <c>id</c> parameter.
        /// </summary>
        [TestMethod]
        public async Task StoredProcedure_GraphQLArgDescription_UsesConfigDescriptionAfterMetadataInit()
        {
            const string entityName = "GetBookWithParamDesc";
            const string configDescription = "The unique identifier for the book (from config)";

            Entity tamperedEntity = new(
                Source: new(
                    "get_book_by_id",
                    EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata>
                    {
                        new() { Name = "id", Description = configDescription }
                    },
                    KeyFields: null),
                GraphQL: new(entityName, entityName, Enabled: true, Operation: GraphQLOperation.Query),
                Rest: new(Enabled: false),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null);

            Dictionary<string, Entity> entityMap = new() { [entityName] = tamperedEntity };
            RuntimeConfig tamperedConfig = _baseConfig with { Entities = new(entityMap) };
            RuntimeConfigProvider tamperedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(tamperedConfig);
            SetUpSQLMetadataProvider(tamperedProvider);
            await _sqlMetadataProvider.InitializeAsync();

            DatabaseObject dbObject = _sqlMetadataProvider.EntityToDatabaseObject[entityName];
            FieldDefinitionNode field = GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema(
                name: new NameNode(entityName),
                entity: tamperedEntity,
                dbObject: dbObject);

            InputValueDefinitionNode idArg = field.Arguments.First(a => a.Name.Value == "id");
            Assert.IsNotNull(idArg.Description);
            Assert.AreEqual(expected: configDescription, actual: idArg.Description!.Value);
        }

        /// <summary>
        /// Verifies that when no description is set on a stored-procedure parameter in the
        /// runtime config the generated GraphQL argument falls back to the default
        /// description text. Exercises the same full pipeline as the positive-case test.
        /// </summary>
        [TestMethod]
        public async Task StoredProcedure_GraphQLArgDescription_FallsBackToDefaultTextWhenNoConfigDescription()
        {
            const string entityName = "GetBookNoDesc";

            Entity tamperedEntity = new(
                Source: new(
                    "get_book_by_id",
                    EntitySourceType.StoredProcedure,
                    Parameters: new List<ParameterMetadata> { new() { Name = "id" } },
                    KeyFields: null),
                GraphQL: new(entityName, entityName, Enabled: true, Operation: GraphQLOperation.Query),
                Rest: new(Enabled: false),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null);

            Dictionary<string, Entity> entityMap = new() { [entityName] = tamperedEntity };
            RuntimeConfig tamperedConfig = _baseConfig with { Entities = new(entityMap) };
            RuntimeConfigProvider tamperedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(tamperedConfig);
            SetUpSQLMetadataProvider(tamperedProvider);
            await _sqlMetadataProvider.InitializeAsync();

            try
            {
                DatabaseObject dbObject = _sqlMetadataProvider.EntityToDatabaseObject[entityName];
                FieldDefinitionNode field = GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema(
                    name: new NameNode(entityName),
                    entity: tamperedEntity,
                    dbObject: dbObject);

                InputValueDefinitionNode idArg = field.Arguments.First(a => a.Name.Value == "id");
                Assert.IsNotNull(idArg.Description);
                Assert.AreEqual(
                    expected: $"parameters for {entityName} stored-procedure",
                    actual: idArg.Description!.Value);
            }
            finally
            {
                RuntimeConfigProvider sharedProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(_baseConfig);
                SetUpSQLMetadataProvider(sharedProvider);
                await _sqlMetadataProvider.InitializeAsync();
            }
        }
    }
}
