using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class MutationBuilderTests
    {
        private Dictionary<string, EntityMetadata> _entityPermissions;

        /// <summary>
        /// Create stub entityPermissions to enable MutationBuilder to create
        /// mutations in GraphQL schema. Without permissions present
        /// (i.e. no roles defined for action on entity), then mutation
        /// will not be created in schema since it is inaccessible
        /// as stated by permissions configuration.
        /// </summary>
        [TestInitialize]
        public void SetupEntityPermissionsMap()
        {
            _entityPermissions = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo", "Baz" },
                    new string[] { ActionType.CREATE, ActionType.UPDATE, ActionType.DELETE },
                    new string[] { "anonymous", "authenticated" }
                    );
        }

        private static Entity GenerateEmptyEntity()
        {
            return new Entity("dbo.entity", Rest: null, GraphQL: null, Array.Empty<PermissionSetting>(), Relationships: new(), Mappings: new());
        }

        [DataTestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Simple Type")]
        [DataRow(new string[] { "authenticated" }, true,
            DisplayName = "Validates @authorize directive is added.")]
        [DataRow(new string[] { "anonymous" }, false,
            DisplayName = "Validates @authorize directive is NOT added.")]
        [DataRow(new string[] { "anonymous", "authenticated" }, false,
            DisplayName = "Validates @authorize directive is NOT added - multiple roles")]
        public void CanGenerateCreateMutationWith_SimpleType(
            IEnumerable<string> roles,
            bool isAuthorizeDirectiveExpected)
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new string[] { ActionType.CREATE },
                    roles);
            DocumentNode mutationRoot = MutationBuilder.Build(root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
            FieldDefinitionNode createField =
                query.Fields.Where(f => f.Name.Value == $"createFoo").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: createField.Directives.Count);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void WillFailToCreateMutationWhenUnrecognisedTypeProvided()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: Date!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DataGatewayException ex = Assert.ThrowsException<DataGatewayException>(
                () => MutationBuilder.Build(root,
                    DatabaseType.cosmos,
                    new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                    entityPermissionsMap: _entityPermissions
                    ),
                "The type Date is not a known GraphQL type, and cannot be used in this schema."
            );
            Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
            Assert.AreEqual(DataGatewayException.SubStatusCodes.GraphQLMapping, ex.SubStatusCode);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationInputName()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual("CreateFooInput", field.Arguments[0].Type.NamedType().Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CreateMutationExcludeIdFromSqlInput_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID! @autoGenerated
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.mssql,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual(1, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[0].Type.NamedType().Name);
            Assert.AreEqual(1, argType.Fields.Count);
            Assert.AreEqual("bar", argType.Fields[0].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CreateMutationIncludeIdCosmosInput_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual(1, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[0].Type.NamedType().Name);
            Assert.AreEqual(2, argType.Fields.Count);
            Assert.AreEqual("id", argType.Fields[0].Name.Value);
            Assert.AreEqual("bar", argType.Fields[1].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Complex Type")]
        public void CanGenerateCreateMutationWith_ComplexType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Complex Type")]
        public void CreateMutationIncludeIdFromInput_ComplexType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(3, inputObj.Fields.Count);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CanGenerateCreateMutationWith_NestedType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: Bar!
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NestedTypeNonNullable()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: Bar!
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("CreateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[1].Type.IsNonNullType(), "bar field shouldn't be null");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NestedTypeNullable()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: Bar
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("CreateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsFalse(inputObj.Fields[1].Type.IsNonNullType(), "bar field should be null");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NestedListTypeNonNullable()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: [Bar!]!
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("CreateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[1].Type.IsNonNullType(), "bar field shouldn't be null");
            Assert.IsTrue(inputObj.Fields[1].Type.InnerType().IsListType(), "bar field should be a list");
            Assert.IsTrue(inputObj.Fields[1].Type.InnerType().InnerType().IsNonNullType(), "list fields aren't nullable");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NullableListTypeNonNullableItems()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: [Bar!]
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("CreateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsFalse(inputObj.Fields[1].Type.IsNonNullType(), "bar field should be null");
            Assert.IsTrue(inputObj.Fields[1].Type.IsListType(), "bar field should be a list");
            Assert.IsTrue(inputObj.Fields[1].Type.InnerType().IsNonNullType(), "list fields aren't nullable");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NestedListTypeNullable()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: [Bar]
}

type Bar {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("CreateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsFalse(inputObj.Fields[1].Type.IsNonNullType(), "bar field should be null");
            Assert.IsTrue(inputObj.Fields[1].Type.IsListType(), "bar field should be a list");
            Assert.IsFalse(inputObj.Fields[1].Type.InnerType().IsNonNullType(), "list fields are nullable");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationExcludeAutoGeneratedFieldFromInput()
        {
            string gql =
                @"
type Foo @model {
    primaryKey: ID! @primaryKey @autoGenerated
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Entity entity = GenerateEmptyEntity();
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.mssql,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);

            Assert.AreEqual(1, inputObj.Fields.Count);
            Assert.AreEqual("bar", inputObj.Fields[0].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationIncludePrimaryKeyOnInputIfNotAutoGenerated()
        {
            string gql =
                @"
type Foo @model {
    primaryKey: ID! @primaryKey
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Entity entity = GenerateEmptyEntity();
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);

            Assert.AreEqual(2, inputObj.Fields.Count);
            Assert.AreEqual("primaryKey", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Delete")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CanGenerateDeleteMutationWith_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"deleteFoo"));
        }

        [DataTestMethod]
        [TestCategory("Mutation Builder - Delete")]
        [TestCategory("Schema Builder - Simple Type")]
        [DataRow(new string[] { "authenticated" }, true,
            DisplayName = "Validates @authorize directive is added.")]
        [DataRow(new string[] { "anonymous" }, false,
            DisplayName = "Validates @authorize directive is NOT added.")]
        [DataRow(new string[] { "anonymous", "authenticated" }, false,
            DisplayName = "Validates @authorize directive is NOT added - multiple roles")]
        public void DeleteMutationIdAsInput_SimpleType(
            IEnumerable<string> roles,
            bool isAuthorizeDirectiveExpected)
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new string[] { ActionType.DELETE },
                    roles);
            DocumentNode mutationRoot = MutationBuilder.Build(root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"deleteFoo");
            Assert.AreEqual(2, field.Arguments.Count);
            Assert.AreEqual("id", field.Arguments[0].Name.Value);
            Assert.AreEqual("ID", field.Arguments[0].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[0].Type.IsNonNullType());
            Assert.AreEqual("_partitionKeyValue", field.Arguments[1].Name.Value);
            Assert.AreEqual("String", field.Arguments[1].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[1].Type.IsNonNullType());

            FieldDefinitionNode deleteField =
                query.Fields.Where(f => f.Name.Value == $"deleteFoo").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: deleteField.Directives.Count);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Update")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CanGenerateUpdateMutationWith_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"updateFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Update")]
        [TestCategory("Schema Builder - Simple Type")]
        public void UpdateMutationInputAllFieldsOptionable_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            Assert.AreEqual(3, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[2].Type.NamedType().Name);
            Assert.AreEqual(2, argType.Fields.Count);
            Assert.AreEqual("id", argType.Fields[0].Name.Value);
            Assert.AreEqual("bar", argType.Fields[1].Name.Value);
        }

        [DataTestMethod]
        [TestCategory("Mutation Builder - Update")]
        [TestCategory("Schema Builder - Simple Type")]
        [DataRow(new string[] { "authenticated" }, true,
            DisplayName = "Validates @authorize directive is added.")]
        [DataRow(new string[] { "anonymous" }, false,
            DisplayName = "Validates @authorize directive is NOT added.")]
        [DataRow(new string[] { "anonymous", "authenticated" }, false,
            DisplayName = "Validates @authorize directive is NOT added - multiple roles")]
        public void UpdateMutationIdFieldAsArgument_SimpleType(
            IEnumerable<string> roles,
            bool isAuthorizeDirectiveExpected)
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new string[] { ActionType.UPDATE },
                    roles);
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            Assert.AreEqual(3, field.Arguments.Count);
            Assert.AreEqual("id", field.Arguments[0].Name.Value);
            Assert.AreEqual("ID", field.Arguments[0].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[0].Type.IsNonNullType());
            Assert.AreEqual("_partitionKeyValue", field.Arguments[1].Name.Value);
            Assert.AreEqual("String", field.Arguments[1].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[1].Type.IsNonNullType());

            FieldDefinitionNode collectionField =
                query.Fields.Where(f => f.Name.Value == $"updateFoo").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: collectionField.Directives.Count);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Updated")]
        [TestCategory("Schema Builder - Nested Type")]
        public void UpdateMutationWithNestedInnerObject_NonNullableListTypeNonNullableItems()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: [Bar!]!
}

type Bar {
    baz: Int
}
                ";

            (DocumentNode mutationRoot, FieldDefinitionNode field) = GenerateTestMutationFieldNodes(gql);
            InputValueDefinitionNode inputArg = field.Arguments[2];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[0].Type.IsNonNullType(), "id field shouldn't be null");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("UpdateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsTrue(inputObj.Fields[1].Type.IsNonNullType(), "bar field shouldn't be null");
            Assert.IsTrue(inputObj.Fields[1].Type.InnerType().IsListType(), "bar field should be a list");
            Assert.IsTrue(inputObj.Fields[1].Type.InnerType().InnerType().IsNonNullType(), "list fields aren't nullable");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Updated")]
        [TestCategory("Schema Builder - Nested Type")]
        public void UpdateMutationWithNestedInnerObject_NullableListTypeNullableItems()
        {
            string gql =
                @"
type Foo @model {
    id: ID
    bar: [Bar]
}

type Bar {
    baz: Int
}
                ";

            (DocumentNode mutationRoot, FieldDefinitionNode field) = GenerateTestMutationFieldNodes(gql);
            InputValueDefinitionNode inputArg = field.Arguments[2];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);

            Assert.AreEqual("id", inputObj.Fields[0].Name.Value);
            Assert.AreEqual("ID", inputObj.Fields[0].Type.NamedType().Name.Value);
            Assert.IsFalse(inputObj.Fields[0].Type.IsNonNullType(), "id field should allow null for schema validation");

            Assert.AreEqual("bar", inputObj.Fields[1].Name.Value);
            Assert.AreEqual("UpdateBarInput", inputObj.Fields[1].Type.NamedType().Name.Value);
            Assert.IsFalse(inputObj.Fields[1].Type.IsNonNullType(), "bar field shouldn't be null");
            Assert.IsTrue(inputObj.Fields[1].Type.IsListType(), "bar field should be a list");
            Assert.IsFalse(inputObj.Fields[1].Type.InnerType().IsNonNullType(), "list fields should be nullable");
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationWontCreateNestedModelsOnInput()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    baz: Baz!
}

type Baz @model {
    id: ID!
    x: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.mssql,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Baz", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual(1, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[0].Type.NamedType().Name);
            Assert.AreEqual(1, argType.Fields.Count);
            Assert.AreEqual("id", argType.Fields[0].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationWillCreateNestedModelsOnInputForCosmos()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    baz: Baz!
}

type Baz @model {
    id: ID!
    x: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                    root,
                    DatabaseType.cosmos,
                    new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Baz", GenerateEmptyEntity() } },
                    entityPermissionsMap: _entityPermissions
                    );
            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual(1, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[0].Type.NamedType().Name);
            Assert.AreEqual(2, argType.Fields.Count);
            Assert.AreEqual("id", argType.Fields[0].Name.Value);
            Assert.AreEqual("baz", argType.Fields[1].Name.Value);
        }

        [DataTestMethod]
        [DataRow(1, "int", "Int")]
        [DataRow("test", "string", "String")]
        [DataRow(true, "boolean", "Boolean")]
        [DataRow(1.2f, "float", "Float")]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationWillHonorDefaultValue(object defaultValue, string fieldName, string fieldType)
        {
            string gql =
                @$"
type Foo @model {{
    id: {fieldType}! @defaultValue(value: {{ {fieldName}: {(defaultValue is string ? $"\"{defaultValue}\"" : defaultValue)} }})
}}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            InputObjectTypeDefinitionNode createFooInput = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name.Value == "CreateFooInput");

            // Serialization has them as strings, so we'll just do string compares
            Assert.AreEqual(defaultValue.ToString(), createFooInput.Fields[0].DefaultValue.Value);
        }

        public static ObjectTypeDefinitionNode GetMutationNode(DocumentNode mutationRoot)
        {
            return (ObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Mutation");

        }

        private (DocumentNode mutationRoot, FieldDefinitionNode field) GenerateTestMutationFieldNodes(string gql)
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                DatabaseType.cosmos,
                new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } },
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            return (mutationRoot, field);
        }
    }
}
