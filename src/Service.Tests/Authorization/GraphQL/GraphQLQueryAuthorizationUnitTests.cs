// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    [TestClass]
    public class GraphQLQueryAuthorizationUnitTests
    {
        /// <summary>
        /// Ensures the authorize directive is present on the ObjectTypeDefinition
        /// with the expected collection of roles resolved from the EntityPermissionsMap.
        /// </summary>
        /// <param name="roles"></param>
        /// <param name="expectedAuthorizeDirective"></param>
        [DataRow(new string[] { }, "", DisplayName = "No Roles -> Expects no objectTypeDefinition created")]
        [DataRow(new string[] { "role1" }, @"@authorize(roles: [""role1""])", DisplayName = "One Role added to Authorize Directive")]
        [DataRow(new string[] { "role1", "role2" }, @"@authorize(roles: [""role1"",""role2""])", DisplayName = "Two Roles added to Authorize Directive")]
        [TestMethod]
        public void AuthorizeDirectiveAddedForQuery(string[] rolesDefinedInPermissions, string expectedAuthorizeDirective)
        {
            string gql =
@"
type Foo @model(name: ""Foo""){
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.MSSQL }
            };

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabasetype,
                entities: new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
                inputTypes: new(),
                GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new string[] { "Foo" },
                    operations: new EntityActionOperation[] { EntityActionOperation.Read },
                    roles: rolesDefinedInPermissions)
                );

            ObjectTypeDefinitionNode query = QueryBuilderTests.GetQueryNode(queryRoot);

            // No roles defined for entity means that GetAll and ByPK queries
            // will NOT be generated, since they're inaccessible.
            // Consequently, no @authorize directive will be present.
            //
            if (rolesDefinedInPermissions.Length == 0)
            {
                Assert.AreEqual(0, query.Fields.Count(), message: "GetAll and ByPK FieldDefinitions Generated Unexpectedly.");
            }
            else
            {
                // Iterate over the GetAll and ByPK queries created by QueryBuilder.Build()
                //
                foreach (FieldDefinitionNode queryField in query.Fields)
                {
                    GraphQLTestHelpers.ValidateAuthorizeDirectivePresence(GraphQLUtils.OBJECT_TYPE_QUERY, rolesDefinedInPermissions, queryField);
                }
            }
        }
    }
}
