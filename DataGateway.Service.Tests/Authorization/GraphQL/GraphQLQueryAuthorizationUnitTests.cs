using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Tests.GraphQLBuilder;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization.GraphQL
{
    [TestClass]
    public class GraphQLQueryAuthorizationUnitTests
    {
        #region Positive Tests
        /// <summary>
        /// Ensures the authorize directive is present on the ObjectTypeDefinition
        /// with the expected collection of roles resolved from the EntityPermissionsMap.
        /// </summary>
        /// <param name="roles"></param>
        /// <param name="expectedAuthorizeDirective"></param>
        [DataRow(new string[] { }, "", DisplayName = "No Roles -> Expects no objectTypeDefinition created")]
        [DataRow(new string[] { "role1" }, @"@authorize(roles: [""role1""])", DisplayName = "One Role added to Authorize Directive")]
        [DataRow(new string[] { "role1", "role2" }, @"@authorize(roles: [""role1"",""role2""])", DisplayName = "Two Roles added to Authorize Directive")]
        [DataTestMethod]
        public void AuthorizeDirectiveAddedForQuery(string[] rolesDefinedInPermissions, string expectedAuthorizeDirective)
        {
            string gql =
    @"
type Foo @model(name: ""Foo""){
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entities: new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: CreateStubEntityPermissionsMap("Foo", "read", rolesDefinedInPermissions));

            ObjectTypeDefinitionNode query = QueryBuilderTests.GetQueryNode(queryRoot);

            // No roles defined for entity means that GetAll and ByPK queries
            // will NOT be generated, since they're inaccessible.
            // Consequently, no @authorize directive will be present.
            //
            if (rolesDefinedInPermissions.Length == 0)
            {
                Assert.IsTrue(query.Fields.Count() == 0, message: "GetAll and ByPK FieldDefinitions Generated Unexpectedly.");
            }
            else
            {
                // Iterate over the GetAll and ByPK queries created by QueryBuilder.Build()
                //
                foreach (FieldDefinitionNode queryField in query.Fields)
                {
                    IEnumerable<DirectiveNode> authorizeDirectiveNodesFound = queryField.Directives.Where(f => f.Name.Value == "authorize");

                    // Currently, only 1 authorize directive node is supported on field definition.
                    //
                    Assert.AreEqual(expected: 1, actual: authorizeDirectiveNodesFound.Count());

                    DirectiveNode authorizationDirectiveNode = authorizeDirectiveNodesFound.First();

                    // Possible Arguments: "roles" and "policy" per:
                    // https://github.com/ChilliCream/hotchocolate/blob/main/src/HotChocolate/AspNetCore/src/AspNetCore.Authorization/AuthorizeDirective.cs
                    //
                    IEnumerable<ArgumentNode> authorizeArguments = authorizationDirectiveNode.Arguments.Where(f => f.Name.Value == "roles");
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
        #endregion
        #region Negative Tests

        #endregion

        /// <summary>
        /// Mock the entityPermissionsMap which resolves which roles need to be included
        /// in an authorize directive used on a GraphQL object type definition.
        /// </summary>
        /// <param name="entityName">Entity for which authorization permissions need to be resolved.</param>
        /// <param name="actionName">Name of action performed on entity to resolve authorization permissions.</param>
        /// <param name="roles">Collection of role names allowed to perform action on entity.</param>
        /// <returns>EntityPermissionsMap Key/Value collection.</returns>
        private static Dictionary<string, EntityMetadata> CreateStubEntityPermissionsMap(string entityName, string actionName, IEnumerable<string> roles )
        {
            EntityMetadata entityMetadata = new()
            {
                ActionToRolesMap = new Dictionary<string, List<string>>()
            };
            entityMetadata.ActionToRolesMap.Add(actionName, roles.ToList());

            Dictionary<string, EntityMetadata> entityPermissionsMap = new();
            entityPermissionsMap.Add(entityName, entityMetadata);

            return entityPermissionsMap;
        }

        private static Entity GenerateEmptyEntity()
        {
            return new Entity("foo", Rest: null, GraphQL: null, Array.Empty<PermissionSetting>(), Relationships: new(), Mappings: new());
        }
    }
}
