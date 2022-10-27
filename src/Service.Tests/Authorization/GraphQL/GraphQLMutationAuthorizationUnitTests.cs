using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config;
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
        /// <param name="operationType"></param>
        /// <param name="rolesDefinedInPermissions"></param>
        /// <param name="expectedAuthorizeDirective"></param>
        [DataRow(Operation.Create, new string[] { }, "",
            DisplayName = "No Roles -> Expects no objectTypeDefinition created")]
        [DataRow(Operation.Create, new string[] { "role1" }, @"@authorize(roles: [""role1""])",
            DisplayName = "One Role added to Authorize Directive")]
        [DataRow(Operation.Create, new string[] { "role1", "role2" }, @"@authorize(roles: [""role1"",""role2""])",
            DisplayName = "Two Roles added to Authorize Directive")]
        [DataTestMethod]
        public void AuthorizeDirectiveAddedForMutation(Operation operationType, string[] rolesDefinedInPermissions, string expectedAuthorizeDirective)
        {
            string gql =
@"
type Foo @model(name: ""Foo""){
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.mssql,
                entities: new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                entityPermissionsMap: GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: new string[] { "Foo" },
                    operations: new Operation[] { operationType },
                    roles: rolesDefinedInPermissions)
                );

            if (rolesDefinedInPermissions.Length > 0)
            {
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
                Assert.AreEqual(rolesDefinedInPermissions.Length, mutationRoot.Definitions.Count());
            }
        }
    }
}
