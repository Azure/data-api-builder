// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    [TestClass]
    public class GraphQLMutationAuthorizationUnitTests
    {
        /// <summary>
        /// Ensures the authorize directive is present on the ObjectTypeDefinition
        /// with the expected collection of roles resolved from the EntityPermissionsMap.
        /// </summary>
        /// <param name="HttpMethod"></param>
        /// <param name="rolesDefinedInPermissions"></param>
        /// <param name="expectedAuthorizeDirective"></param>
        [DataRow(EntityActionOperation.Create, new string[] { }, "",
            DisplayName = "No Roles -> Expects no objectTypeDefinition created")]
        [DataRow(EntityActionOperation.Create, new string[] { "role1" }, @"@authorize(roles: [""role1""])",
            DisplayName = "One Role added to Authorize Directive")]
        [DataRow(EntityActionOperation.Create, new string[] { "role1", "role2" }, @"@authorize(roles: [""role1"",""role2""])",
            DisplayName = "Two Roles added to Authorize Directive")]
        [TestMethod]
        public void AuthorizeDirectiveAddedForMutation(EntityActionOperation HttpMethod, string[] rolesDefinedInPermissions, string expectedAuthorizeDirective)
        {
            string entityName = "Foo";

            string gql =
@"
type Foo @model(name: ""Foo""){
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { entityName, DatabaseType.MSSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                entities: new(new Dictionary<string, Entity> { { entityName, GraphQLTestHelpers.GenerateEmptyEntity() } }),
                entityPermissionsMap: GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new string[] { entityName },
                    operations: new EntityActionOperation[] { HttpMethod },
                    roles: rolesDefinedInPermissions)
                );

            if (rolesDefinedInPermissions.Length > 0)
            {
                Assert.IsGreaterThan(0, mutationRoot.Definitions.Count());
                ObjectTypeDefinitionNode mutation = MutationBuilderTests.GetMutationNode(mutationRoot);
                // Iterate over the mutations created by MutationBuilder.Build()
                //
                foreach (FieldDefinitionNode mutationField in mutation.Fields)
                {
                    GraphQLTestHelpers.ValidateAuthorizeDirectivePresence(GraphQLUtils.OBJECT_TYPE_MUTATION, rolesDefinedInPermissions, mutationField);
                }
            }
            else
            {
                Assert.AreEqual(0, mutationRoot.Definitions.Count());
            }
        }
    }
}
