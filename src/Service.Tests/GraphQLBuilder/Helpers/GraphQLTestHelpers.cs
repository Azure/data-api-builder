// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers
{
    public static class GraphQLTestHelpers
    {
        public const string BOOK_GQL =
                    @"
                    type Book @model(name:""Book"") {
                        book_id: Int! @primaryKey
                    }
                    ";

        public const string BOOKS_GQL =
                    @"
                    type Books @model(name:""Books"") {
                        book_id: Int! @primaryKey
                    }
                    ";

        public const string PERSON_GQL =
                    @"
                    type Person @model(name:""Person"") {
                        person_id: Int! @primaryKey
                    }
                    ";

        public const string PEOPLE_GQL =
                    @"
                    type People @model(name:""People"") {
                        people_id: Int! @primaryKey
                    }
                    ";

        /// <summary>
        /// Mock the entityPermissionsMap which resolves which roles need to be included
        /// in an authorize directive used on a GraphQL object type definition.
        /// </summary>
        /// <param name="entityNames">Entities for which authorization permissions need to be resolved.</param>
        /// <param name="operations">Actions performed on entity to resolve authorization permissions.</param>
        /// <param name="roles">Collection of role names allowed to perform action on entity.</param>
        /// <returns>EntityPermissionsMap Key/Value collection.</returns>
        public static Dictionary<string, EntityMetadata> CreateStubEntityPermissionsMap(string[] entityNames, IEnumerable<EntityActionOperation> operations, IEnumerable<string> roles)
        {
            EntityMetadata entityMetadata = new()
            {
                OperationToRolesMap = new Dictionary<EntityActionOperation, List<string>>()
            };

            foreach (EntityActionOperation operation in operations)
            {
                entityMetadata.OperationToRolesMap.Add(operation, roles.ToList());
            }

            Dictionary<string, EntityMetadata> entityPermissionsMap = new();

            foreach (string entityName in entityNames)
            {
                entityPermissionsMap.Add(entityName, entityMetadata);
            }

            return entityPermissionsMap;
        }

        /// <summary>
        /// Creates an empty entity with no permissions or exposed rest/graphQL endpoints.
        /// </summary>
        /// <param name="sourceType">type of source object. Default is Table.</param>
        public static Entity GenerateEmptyEntity(EntitySourceType sourceType = EntitySourceType.Table)
        {
            return new Entity(Source: new EntitySource(Type: sourceType, Object: "foo", Parameters: null, KeyFields: null),
                              Rest: new(Array.Empty<SupportedHttpVerb>()),
                              GraphQL: new("", ""),
                              Permissions: Array.Empty<EntityPermission>(),
                              Relationships: new(),
                              Mappings: new());
        }

        /// <summary>
        /// Creates a stored procedure backed entity using the supplied metadata.
        /// </summary>
        /// <param name="graphQLTypeName">Desired GraphQL type name.</param>
        /// <param name="graphQLOperation">Query or Mutation</param>
        /// <param name="permissionOperations">Collection of permission operations (CRUD+Execute). Default: Execute</param>
        /// <param name="dbObjectName">Name of object in the database. Default: foo</param>
        /// <param name="parameters">Collection of defined stored procedure parameters</param>
        /// <returns>Stored procedure backed entity.</returns>
        public static Entity GenerateStoredProcedureEntity(
            string graphQLTypeName,
            GraphQLOperation? graphQLOperation,
            string[] permissionOperations = null,
            string dbObjectName = "foo",
            List<ParameterMetadata> parameters = null
            )
        {
            IEnumerable<EntityAction> actions = (permissionOperations ?? new string[] { }).Select(a => new EntityAction(EnumExtensions.Deserialize<EntityActionOperation>(a), null, new(null, null)));
            Entity entity = new(Source: new EntitySource(Type: EntitySourceType.StoredProcedure, Object: "foo", Parameters: parameters, KeyFields: null),
                              Rest: new(Array.Empty<SupportedHttpVerb>()),
                              GraphQL: new(Singular: graphQLTypeName, Plural: "", Enabled: true, Operation: graphQLOperation),
                              Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: actions.ToArray()) },
                              Relationships: new(),
                              Mappings: new());
            return entity;
        }

        /// <summary>
        /// Creates an entity with a SingularPlural GraphQL type.
        /// </summary>
        /// <param name="singularNameForEntity"> Singular name defined by user in the config.</param>
        /// <param name="pluralNameForEntity"> Plural name defined by user in the config.</param>
        /// <param name="sourceType">type of source object. Default is Table.</param>
        public static Entity GenerateEntityWithSingularPlural(string singularNameForEntity, string pluralNameForEntity, EntitySourceType sourceType = EntitySourceType.Table)
        {
            return new Entity(Source: new EntitySource(Type: sourceType, Object: "foo", Parameters: null, KeyFields: null),
                              Rest: new(Array.Empty<SupportedHttpVerb>()),
                              GraphQL: new(singularNameForEntity, pluralNameForEntity),
                              Permissions: Array.Empty<EntityPermission>(),
                              Relationships: new(),
                              Mappings: new());
        }

        /// <summary>
        /// Creates an entity with a string GraphQL type.
        /// </summary>
        /// <param name="singularGraphQLName"></param>
        /// <param name="sourceType">type of source object. Default is Table.</param>
        /// <returns></returns>
        public static Entity GenerateEntityWithStringType(string singularGraphQLName, EntitySourceType sourceType = EntitySourceType.Table)
        {
            return new Entity(Source: new EntitySource(Type: sourceType, Object: "foo", Parameters: null, KeyFields: null),
                              Rest: new(Array.Empty<SupportedHttpVerb>()),
                              GraphQL: new(singularGraphQLName, ""),
                              Permissions: Array.Empty<EntityPermission>(),
                              Relationships: new(),
                              Mappings: new());
        }

        /// <summary>
        /// Ensures that for each fieldDefinition present:
        /// - One @authorize directive found
        /// - 1 "roles" argument found on authorize directive
        /// - roles defined on directive are the expected roles defined in runtime configuration
        /// </summary>
        /// <param name="ObjectType">Query or Mutation</param>
        /// <param name="fieldDefinition">Query or Mutation Definition</param>
        public static void ValidateAuthorizeDirectivePresence(string ObjectType, IEnumerable<string> rolesDefinedInPermissions, FieldDefinitionNode fieldDefinition)
        {
            IEnumerable<DirectiveNode> authorizeDirectiveNodesFound = fieldDefinition.Directives.Where(f => f.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE);

            // Currently, only 1 authorize directive node is supported on field definition.
            //
            Assert.AreEqual(expected: 1, actual: authorizeDirectiveNodesFound.Count());

            DirectiveNode authorizationDirectiveNode = authorizeDirectiveNodesFound.First();

            // Possible Arguments: "roles" and "policy" per:
            // https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/AuthorizeDirective.cs
            //
            IEnumerable<ArgumentNode> authorizeArguments = authorizationDirectiveNode.Arguments.Where(f => f.Name.Value == GraphQLUtils.AUTHORIZE_DIRECTIVE_ARGUMENT_ROLES);
            Assert.AreEqual(expected: 1, actual: authorizeArguments.Count());

            ArgumentNode roleArgumentNode = authorizeArguments.First();

            // roleArgumentNode.Value of type IValueNode implemented as a ListValueNode (of role names) for this DirectiveType.
            // ListValueNode collection elements are in the Items property.
            // Items is a collection of IValueNodes which represent role names.
            // Each Item has a Value property of type object, which get casted to a string.
            //
            IEnumerable<string> rolesInRoleArgumentNode = ((ListValueNode)roleArgumentNode.Value).Items.Select(f => (string)f.Value);

            // Ensure expected roles are present in the authorize directive.
            Assert.IsTrue(Enumerable.SequenceEqual(first: rolesDefinedInPermissions, second: rolesInRoleArgumentNode));
        }
    }
}
