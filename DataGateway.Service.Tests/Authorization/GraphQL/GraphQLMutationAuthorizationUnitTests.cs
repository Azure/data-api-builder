using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.Tests.GraphQLBuilder;
using Azure.DataGateway.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization.GraphQL
{
    [TestClass]
    public class GraphQLMutationAuthorizationUnitTests
    {
        /// <summary>
        /// Ensures the authorize directive is present on the ObjectTypeDefinition
        /// with the expected collection of roles resolved from the EntityPermissionsMap.
        /// </summary>
        /// <param name="roles"></param>
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
                    actions: new Operation[] { operationType },
                    roles: rolesDefinedInPermissions)
                );

            ObjectTypeDefinitionNode mutation = MutationBuilderTests.GetMutationNode(mutationRoot);

            // No roles defined for entity means that create mutation
            // will NOT be generated, since it's inaccessible.
            // Consequently, no @authorize directive will be present.
            //
            if (rolesDefinedInPermissions.Length == 0)
            {
                Assert.IsTrue(mutation.Fields.Select(f => f.Name.Value == "createFoo").Count() == 0, message: $"{operationType} FieldDefinition Generated Unexpectedly.");
            }
            else
            {
                // Iterate over the mutations created by MutationBuilder.Build()
                //
                foreach (FieldDefinitionNode mutationField in mutation.Fields)
                {
                    GraphQLTestHelpers.ValidateAuthorizeDirectivePresence(GraphQLUtils.OBJECT_TYPE_MUTATION, rolesDefinedInPermissions, mutationField);
                }
            }
        }
    }
}
