// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Sql;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Sql
{
    /// <summary>
    /// Validate GraphQL schema creation for stored procedure entities.
    /// </summary>
    [TestClass]
    public class StoredProcedureBuilderTests
    {
        private Dictionary<string, EntityMetadata> _entityPermissions;

        /// <summary>
        /// Validates that a default value for stored procedure parameter defined in the runtime config
        /// casts to the value type defined in the database schema because inferring the degree of precision
        /// and associated value type of a numeric value written in the config is not sufficient.
        /// - byte[] example referenced from Microsoft Docs example of Convert.FromBase64String(String) because
        /// a byte array would be represented in a JSON payload as a string.
        /// </summary>
        /// <seealso cref="https://learn.microsoft.com/dotnet/api/system.convert.frombase64string?view=net-6.0#examples"/>
        /// <seealso cref="https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/floating-point-numeric-types"/>
        /// <param name="systemType">Denotes system value type of stored procedure parameter.</param>
        /// <param name="expectedGraphQLType">Target GraphQL type of parameter.</param>
        /// <param name="configParamValue">Explicit parameter default value set in runtime configuration.</param>
        [DataTestMethod]
        [DataRow(typeof(byte), BYTE_TYPE, 64, false, DisplayName = "Byte")]
        [DataRow(typeof(short), SHORT_TYPE, 32767, false, DisplayName = "Short")]
        [DataRow(typeof(int), INT_TYPE, 2147483647, false, DisplayName = "Int")]
        [DataRow(typeof(long), LONG_TYPE, 9223372036854775807, false, DisplayName = "Long")]
        [DataRow(typeof(float), SINGLE_TYPE, 3.402823e38, false, DisplayName = "Single")]
        [DataRow(typeof(double), FLOAT_TYPE, "12.7", false, DisplayName = "Float")]
        [DataRow(typeof(decimal), DECIMAL_TYPE, "12.7", false, DisplayName = "Decimal")]
        [DataRow(typeof(string), STRING_TYPE, "paramValueconfig", false, DisplayName = "String")]
        [DataRow(typeof(bool), BOOLEAN_TYPE, true, false, DisplayName = "Bool")]
        [DataRow(typeof(bool), BOOLEAN_TYPE, "dog", true, DisplayName = "Bool")]
        [DataRow(typeof(DateTime), DATETIME_TYPE, "12/31/2030 12:00:00 AM", false, DisplayName = "DateTime")]
        [DataRow(typeof(DateTime), DATETIME_TYPE, "12/31/2030 12000 AM", true, DisplayName = "DateTime")]
        [DataRow(typeof(DateTimeOffset), DATETIME_TYPE, "11/19/2012 10:57:11 AM -08:00", false, DisplayName = "DateTimeOffset")]
        [DataRow(typeof(TimeOnly), LOCALTIME_TYPE, "10:57:11.0000", false, DisplayName = "LocalTime")]
        [DataRow(typeof(byte[]), BYTEARRAY_TYPE, "AgQGCAoMDhASFA==", false, DisplayName = "Byte[]")]
        [DataRow(typeof(Guid), UUID_TYPE, "f58b7b58-62c9-4b97-ab60-75de70793f66", false, DisplayName = "GraphQL UUID/ SystemType GUID")]
        [DataRow(typeof(string), STRING_TYPE, "f58b7b58-62c9-4b97-ab60-75de70793f66", false, DisplayName = "DB/SystemType String -> GUID value -> Resolve as GraphQL string")]
        public void StoredProcedure_ParameterValueTypeResolution(
            Type systemType,
            string expectedGraphQLType,
            object configParamValue,
            bool expectsError)
        {
            // Arbitrary generic names to be used for database metadata.
            string parameterName = "parameter1";
            string outputColumnName = "col1";
            string spQueryTypeName = "StoredProcedureQueryType";
            string spMutationTypeName = "StoredProcedureMutationType";

            // Hydrate a DatabaseObject containing relevant stored procedure metadata (parameter and column names and value types)
            // used to create a GraphQL schema object.
            Dictionary<string, ParameterDefinition> dbSourcedParameters = new() { { parameterName, new() { SystemType = systemType } } };
            DatabaseObject spDbObj = new DatabaseStoredProcedure(schemaName: "dbo", tableName: "dbObjectName")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new()
                {
                    Parameters = dbSourcedParameters
                }
            };

            spDbObj.SourceDefinition.Columns.TryAdd(outputColumnName, new() { SystemType = systemType });

            // Parameter collection used to create DatabaseObjectSource which is used to create a new entity object.
            Dictionary<string, object> configSourcedParameters = new() { { parameterName, JsonSerializer.SerializeToElement(configParamValue) } };

            // Create a new entity where the GraphQL type is explicitly defined as Mutation in the runtime config.
            Entity spMutationEntity = GraphQLTestHelpers.GenerateStoredProcedureEntity(
                graphQLTypeName: spMutationTypeName,
                graphQLOperation: GraphQLOperation.Mutation,
                parameters: configSourcedParameters);

            // Create a new entity where the GraphQL type is explicitly defined as Query in the runtime config.
            Entity spQueryEntity = GraphQLTestHelpers.GenerateStoredProcedureEntity(
                graphQLTypeName: spQueryTypeName,
                graphQLOperation: GraphQLOperation.Query,
                parameters: configSourcedParameters);

            // Create the GraphQL type for the stored procedure entity.
            string spQueryEntityName = "spquery";
            ObjectTypeDefinitionNode spQueryObjectTypeDefinition = CreateGraphQLTypeForEntity(spQueryEntity, spQueryEntityName, spDbObj);
            string spMutationEntityName = "spmutation";
            ObjectTypeDefinitionNode spMutationObjectTypeDefinition = CreateGraphQLTypeForEntity(spMutationEntity, spMutationEntityName, spDbObj);

            // Create the root schema document with the previously created stored procedure type.
            Dictionary<string, ObjectTypeDefinitionNode> objectTypeDefinitions = new()
            {
                { spQueryEntityName, spQueryObjectTypeDefinition },
                { spMutationEntityName, spMutationObjectTypeDefinition }
            };

            DocumentNode root = CreateGraphQLDocument(objectTypeDefinitions);

            // Create permissions and entities collections used within the mutation and query builders.
            _entityPermissions = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                entityNames: new[] { spQueryEntityName, spMutationEntityName },
                operations: new[] { EntityActionOperation.Execute },
                roles: SchemaConverterTests.GetRolesAllowedForEntity()
                );
            Dictionary<string, Entity> entities = new()
            {
                { spMutationEntityName, spMutationEntity },
                { spQueryEntityName, spQueryEntity }
            };

            try
            {
                Dictionary<string, DatabaseType> entityToDatabaseName = new()
                {
                    {"Foo", DatabaseType.MSSQL }
                };

                // Build GraphQL schema document for the mutation which hydrates parameter metadata and
                // attempts to convert the parameter value provided in configuration
                // to the value type denoted in the database schema (metadata supplied via DatabaseObject).
                DocumentNode mutationRoot = MutationBuilder.Build(
                    root,
                    entityToDatabaseName,
                    entities: new(entities),
                    entityPermissionsMap: _entityPermissions,
                    dbObjects: new Dictionary<string, DatabaseObject> { { spMutationEntityName, spDbObj } }
                );

                // Get mutation object type definition and validate its contents.
                ObjectTypeDefinitionNode mutation = MutationBuilderTests.GetMutationNode(mutationRoot);
                ValidateStoredProcedureSchema(typeNode: mutation, expectedGraphQLType, spMutationTypeName, parameterName);

                // Build GraphQL schema document for the query which hydrates parameter metadata and
                // attempts to convert the parameter value provided in configuration
                // to the value type denoted in the database schema (metadata supplied via DatabaseObject).
                DocumentNode queryRoot = QueryBuilder.Build(
                    root,
                    entityToDatabaseName,
                    entities: new(entities),
                    inputTypes: null,
                    entityPermissionsMap: _entityPermissions,
                    dbObjects: new Dictionary<string, DatabaseObject> { { spQueryEntityName, spDbObj } }
                );

                // Get query object type definition and validate its contents.
                ObjectTypeDefinitionNode query = QueryBuilderTests.GetQueryNode(queryRoot);
                ValidateStoredProcedureSchema(typeNode: query, expectedGraphQLType, spQueryTypeName, parameterName);

                Assert.IsFalse(expectsError, message: $"Failure: Expected a value type conversion error during stored procedure field creation.");
            }
            catch (DataApiBuilderException ex)
            {
                Assert.IsTrue(expectsError, message: $"Failure: Did not expect error during stored procedure field creation: {ex.Message}");
                Assert.AreEqual(expected: HttpStatusCode.InternalServerError, actual: ex.StatusCode);
                Assert.AreEqual(expected: DataApiBuilderException.SubStatusCodes.GraphQLMapping, actual: ex.SubStatusCode);
            }

        }

        /// <summary>
        /// Creates root document node containing all the supplied object type definitions.
        /// </summary>
        /// <param name="objectTypeDefinitions">Collection: key-> entityName, value-> objectTypeDefinition</param>
        /// <returns>Root DocumentNode</returns>
        public static DocumentNode CreateGraphQLDocument(Dictionary<string, ObjectTypeDefinitionNode> objectTypeDefinitions)
        {
            List<IDefinitionNode> nodes = new(objectTypeDefinitions.Values);
            return new DocumentNode(nodes);
        }

        /// <summary>
        /// Creates an ObjectTypeDefinitionNode representing the supplied entity metadata.
        /// </summary>
        /// <param name="spEntity">Entity object.</param>
        /// <param name="entityName">Entity name.</param>
        /// <param name="spDbObj">DatabaseObject representing stored procedure metadata.</param>
        /// <returns>Stored procedure ObjectTypeDefinitionNode</returns>
        public static ObjectTypeDefinitionNode CreateGraphQLTypeForEntity(Entity spEntity, string entityName, DatabaseObject spDbObj)
        {
            // Output column metadata hydration, parameter entities is used for relationship metadata handling, which is not
            // relevant for stored procedure tests.
            ObjectTypeDefinitionNode objectTypeDefinitionNode = SchemaConverter.GenerateObjectTypeDefinitionForDatabaseObject(
                entityName: entityName,
                spDbObj,
                configEntity: spEntity,
                entities: new(new Dictionary<string, Entity>()),
                rolesAllowedForEntity: SchemaConverterTests.GetRolesAllowedForEntity(),
                rolesAllowedForFields: SchemaConverterTests.GetFieldToRolesMap()
                );

            return objectTypeDefinitionNode;
        }

        /// <summary>
        /// Validates that the passed in GraphQL ObjectTypeDefinitionNode:
        /// - Contains a type field representing the expected stored procedure
        /// - The stored procedure field contains an input value which represents the stored procedure input parameter.
        /// - The GraphQL value type of the input parameter definition is the 
        /// </summary>
        /// <param name="typeNode">Stored procudure object type node.</param>
        /// <param name="expectedGraphQLType">Expected GraphQL value type.</param>
        /// <param name="graphQLTypeName">Name of GraphQL type in schema.</param>
        /// <param name="parameterName">Name of stored procedure parameter.</param>
        public static void ValidateStoredProcedureSchema(
            ObjectTypeDefinitionNode typeNode,
            string expectedGraphQLType,
            string graphQLTypeName,
            string parameterName)
        {
            // Validates that GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema() properly creates an
            // InputValueDefinitionNode for the stored procedure parameter.
            // Also validates that the parameter name used is actually the parameter name from the database schema and not
            // the name of an output result set column name because the names and value types could be different.
            FieldDefinitionNode finalizedField = typeNode.Fields.First(f => f.Name.Value.StartsWith($"execute{graphQLTypeName}"));
            InputValueDefinitionNode parameterArgument = finalizedField.Arguments.First(arg => arg.Name.Value.Equals(parameterName));
            Assert.IsNotNull(parameterArgument, message: $"Failure: InputValueDefinitionNode for parameter '{parameterName}' not found.");

            // Validates that GraphQLUtils.ConvertValueToGraphQLType() converted the default parameter value supplied in configuration
            // to the GraphQL value type aligned to the value type of the parameter in the database schema.
            string actualGraphQLType = parameterArgument.Type.NamedType().Name.Value;
            string mismatchedTypeErrorMsg = $"Failure: Parameter '{parameterName}' is type '{actualGraphQLType}' but should be type '{expectedGraphQLType}'";
            Assert.AreEqual(expected: expectedGraphQLType, actual: actualGraphQLType, message: mismatchedTypeErrorMsg);
        }
    }
}
