// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using HotChocolate.Types;
using Humanizer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
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
                    new string[] { "Foo", "Baz", "Bar" },
                    new EntityActionOperation[] { EntityActionOperation.Create, EntityActionOperation.Update, EntityActionOperation.Delete },
                    new string[] { "anonymous", "authenticated" }
                    );
        }

        private static Entity GenerateEmptyEntity()
        {
            return new Entity(
                Source: new("dbo.entity", EntitySourceType.Table, null, null),
                Rest: new(Enabled: false),
                GraphQL: new("Foo", "Foos", Enabled: true),
                Permissions: Array.Empty<EntityPermission>(),
                Relationships: new(),
                Mappings: new());
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new EntityActionOperation[] { EntityActionOperation.Create },
                    roles);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
            FieldDefinitionNode createField =
                query.Fields.Where(f => f.Name.Value == $"createFoo").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: createField.Directives.Where(x => x.Name.Value is GraphQLUtils.AUTHORIZE_DIRECTIVE).Count());
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void WillFailToCreateMutationWhenUnrecognisedTypeProvided()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
    bar: Date!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => MutationBuilder.Build(root,
                    entityNameToDatabasetype,
                    new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
                    entityPermissionsMap: _entityPermissions
                    ),
                "The type Date is not a known GraphQL type, and cannot be used in this schema."
            );
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        public void CreateMutationInputName()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID! @autoGenerated
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.MSSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: Bar!
}

type Bar @model(name:""Bar""){
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabasetype = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabasetype,
                new(new Dictionary<string, Entity> {
                    { "Foo", GenerateEmptyEntity() with { GraphQL = new("Foo", "Foos") } },
                    { "Bar", GenerateEmptyEntity() with { GraphQL = new("Bar", "Bars") } }
                }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: Bar!
}

type Bar @model(name:""Bar""){
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: Bar
}

type Bar @model(name:""Bar""){
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: [Bar!]!
}

type Bar @model(name:""Bar"") {
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: [Bar!]
}

type Bar @model(name:""Bar""){
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: [Bar]
}

type Bar @model(name:""Bar""){
    baz: Int
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    primaryKey: ID! @primaryKey @autoGenerated
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.MSSQL },
            };

            Entity entity = GenerateEmptyEntity();
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    primaryKey: ID! @primaryKey
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            Entity entity = GenerateEmptyEntity();
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode mutationRoot = MutationBuilder.Build(root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new EntityActionOperation[] { EntityActionOperation.Delete },
                    roles);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode mutationRoot = MutationBuilder.Build(root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
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
                actual: deleteField.Directives.Where(x => x.Name.Value is "authorize").Count());
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Update")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CanGenerateUpdateMutationWith_SimpleType()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new EntityActionOperation[] { EntityActionOperation.Update },
                    roles);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
                actual: field.Directives.Where(x => x.Name.Value is "authorize").Count());
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Updated")]
        [TestCategory("Schema Builder - Nested Type")]
        public void UpdateMutationWithNestedInnerObject_NonNullableListTypeNonNullableItems()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
    bar: [Bar!]!
}

type Bar @model(name:""Bar""){
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
type Foo @model(name:""Foo"") {
    id: ID
    bar: [Bar]
}

type Bar @model(name:""Bar""){
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
        public void CreateMutationWillCreateNestedModelsOnInputForCosmos()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
    baz: Baz!
}

type Baz @model(name:""Baz""){
    id: ID!
    x: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Baz", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode mutationRoot = MutationBuilder.Build(
                    root,
                    entityNameToDatabaseType,
                    new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Baz", GenerateEmptyEntity() } }),
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
type Foo @model(name:""Foo"") {{
    id: {fieldType}! @defaultValue(value: {{ {fieldName}: {(defaultValue is string ? $"\"{defaultValue}\"" : defaultValue)} }})
}}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() } }),
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
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL },
                { "Bar", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GenerateEmptyEntity() }, { "Bar", GenerateEmptyEntity() } }),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            return (mutationRoot, field);
        }

        /// <summary>
        /// We assume that the user will provide a singular name for the entity. Users have the option of providing singular and
        /// plural names for an entity in the config to have more control over the graphql schema generation.
        /// When singular and plural names are specified by the user, these names will be used for generating the
        /// queries and mutations in the schema.
        /// When singular and plural names are not provided, the queries and mutations will be generated with the entity's name.
        /// 
        /// This test validates a) Number of mutation fields generated b) Mutation field names c) Mutation field descriptions
        /// when multiple create operations are disabled.
        /// 
        /// </summary>
        /// <param name="gql">Type definition for the entity</param>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="singularName">Singular name provided by the user</param>
        /// <param name="pluralName">Plural name provided by the user</param>
        /// <param name="expectedName"> Expected name of the entity in the mutation. Used to construct the exact expected mutation names.</param>
        [DataTestMethod]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, null, null, new string[] { "People" },
            DisplayName = "Mutation name and description validation for singular entity name with singular plural not defined - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, new string[] { "Person" }, new string[] { "People" }, new string[] { "Person" },
            DisplayName = "Mutation name and description validation for plural entity name with singular plural defined - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, new string[] { "Person" }, new string[] { "" }, new string[] { "Person" },
            DisplayName = "Mutation name and description validation for plural entity name with singular defined - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL, new string[] { "Person" }, null, null, new string[] { "Person" },
            DisplayName = "Mutation name and description validation for singular entity name with singular plural not defined - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL, new string[] { "Person" }, new string[] { "Person" }, new string[] { "People" }, new string[] { "Person" },
            DisplayName = "Mutation name and description validation for singular entity name with singular plural defined - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL + GraphQLTestHelpers.BOOK_GQL, new string[] { "Person", "Book" }, new string[] { "Person", "Book" }, new string[] { "People", "Books" }, new string[] { "Person", "Book" },
            DisplayName = "Mutation name and description validation for multiple entities with singular, plural - Multiple Create Operation disabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL + GraphQLTestHelpers.BOOK_GQL, new string[] { "Person", "Book" }, null, null, new string[] { "Person", "Book" },
            DisplayName = "Mutation name and description validation for multiple entities with singular plural not defined - Multiple Create Operation disabled")]
        public void ValidateMutationsAreCreatedWithRightNameWithMultipleCreateOperationDisabled(
            string gql,
            string[] entityNames,
            string[] singularNames,
            string[] pluralNames,
            string[] expectedNames
            )
        {
            Dictionary<string, Entity> entityNameToEntity = new();
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new();
            Dictionary<string, EntityMetadata> entityPermissionsMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                entityNames,
                new EntityActionOperation[] { EntityActionOperation.Create, EntityActionOperation.Update, EntityActionOperation.Delete },
                new string[] { "anonymous", "authenticated" });
            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            for (int i = 0; i < entityNames.Length; i++)
            {
                Entity entity = (singularNames is not null)
                                    ? GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularNames[i], pluralNames[i])
                                    : GraphQLTestHelpers.GenerateEntityWithSingularPlural(entityNames[i], entityNames[i].Pluralize());
                entityNameToEntity.TryAdd(entityNames[i], entity);
                entityNameToDatabaseType.TryAdd(entityNames[i], i % 2 == 0 ? DatabaseType.CosmosDB_NoSQL : DatabaseType.MSSQL);

            }

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(entityNameToEntity),
                entityPermissionsMap: entityPermissionsMap,
                IsMultipleCreateOperationEnabled: false
                );

            ObjectTypeDefinitionNode mutation = GetMutationNode(mutationRoot);
            Assert.IsNotNull(mutation);

            // The permissions are setup for create, update and delete operations.
            // So create, update and delete mutations should get generated.
            // A Check to validate that the count of mutations generated is 3 -
            // 1. 1 Create mutation
            // 2. 1 Update mutation
            // 3. 1 Delete mutation
            int totalExpectedMutations = 3 * entityNames.Length;
            Assert.AreEqual(totalExpectedMutations, mutation.Fields.Count);

            for (int i = 0; i < entityNames.Length; i++)
            {
                // Name and Description validations for Create mutation
                string expectedCreateMutationName = $"create{expectedNames[i]}";
                string expectedCreateMutationDescription = $"Creates a new {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedCreateMutationName));
                FieldDefinitionNode createMutation = mutation.Fields.First(f => f.Name.Value == expectedCreateMutationName);
                Assert.AreEqual(expectedCreateMutationDescription, createMutation.Description.Value);

                // Name and Description validations for Update mutation
                string expectedUpdateMutationName = $"update{expectedNames[i]}";
                string expectedUpdateMutationDescription = $"Updates a {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedUpdateMutationName));
                FieldDefinitionNode updateMutation = mutation.Fields.First(f => f.Name.Value == expectedUpdateMutationName);
                Assert.AreEqual(expectedUpdateMutationDescription, updateMutation.Description.Value);

                // Name and Description validations for Delete mutation
                string expectedDeleteMutationName = $"delete{expectedNames[i]}";
                string expectedDeleteMutationDescription = $"Delete a {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedDeleteMutationName));
                FieldDefinitionNode deleteMutation = mutation.Fields.First(f => f.Name.Value == expectedDeleteMutationName);
                Assert.AreEqual(expectedDeleteMutationDescription, deleteMutation.Description.Value);
            }
        }

        /// <summary>
        /// We assume that the user will provide a singular name for the entity. Users have the option of providing singular and
        /// plural names for an entity in the config to have more control over the graphql schema generation.
        /// When singular and plural names are specified by the user, these names will be used for generating the
        /// queries and mutations in the schema.
        /// When singular and plural names are not provided, the queries and mutations will be generated with the entity's name.
        /// 
        /// This test validates a) Number of mutation fields generated b) Mutation field names c) Mutation field descriptions
        /// when multiple create operations are enabled.
        /// 
        /// </summary>
        /// <param name="gql">Type definition for the entity</param>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="singularName">Singular name provided by the user</param>
        /// <param name="pluralName">Plural name provided by the user</param>
        /// <param name="expectedName"> Expected name of the entity in the mutation. Used to construct the exact expected mutation names.</param>
        [DataTestMethod]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, null, null, new string[] { "People" }, new string[] { "Peoples" },
            DisplayName = "Mutation name and description validation for singular entity name with singular plural not defined - Multiple Create Operation enabled")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, new string[] { "Person" }, new string[] { "People" }, new string[] { "Person" }, new string[] { "People" },
            DisplayName = "Mutation name and description validation for plural entity name with singular plural defined - Multiple Create Operation enabled")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, new string[] { "Person" }, new string[] { "" }, new string[] { "Person" }, new string[] { "People" },
            DisplayName = "Mutation name and description validation for plural entity name with singular defined - Multiple Create Operation enabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL, new string[] { "Person" }, new string[] { "Person" }, new string[] { "People" }, new string[] { "Person" }, new string[] { "People" },
            DisplayName = "Mutation name and description validation for singular entity name with singular plural defined - Multiple Create Operation enabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL + GraphQLTestHelpers.BOOK_GQL, new string[] { "Person", "Book" }, new string[] { "Person", "Book" }, new string[] { "People", "Books" }, new string[] { "Person", "Book" }, new string[] { "People", "Books" },
            DisplayName = "Mutation name and description validation for multiple entities with singular, plural - Multiple Create Operation enabled")]
        [DataRow(GraphQLTestHelpers.PERSON_GQL + GraphQLTestHelpers.BOOK_GQL, new string[] { "Person", "Book" }, null, null, new string[] { "Person", "Book" }, new string[] { "People", "Books" },
            DisplayName = "Mutation name and description validation for multiple entities with singular plural not defined - Multiple Create Operation enabled")]
        public void ValidateMutationsAreCreatedWithRightNameWithMultipleCreateOperationsEnabled(
            string gql,
            string[] entityNames,
            string[] singularNames,
            string[] pluralNames,
            string[] expectedNames,
            string[] expectedCreateMultipleMutationNames)
        {
            Dictionary<string, Entity> entityNameToEntity = new();
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new();
            Dictionary<string, EntityMetadata> entityPermissionsMap = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                entityNames,
                new EntityActionOperation[] { EntityActionOperation.Create, EntityActionOperation.Update, EntityActionOperation.Delete },
                new string[] { "anonymous", "authenticated" });
            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            for (int i = 0; i < entityNames.Length; i++)
            {
                Entity entity = (singularNames is not null)
                                    ? GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularNames[i], pluralNames[i])
                                    : GraphQLTestHelpers.GenerateEntityWithSingularPlural(entityNames[i], entityNames[i].Pluralize());
                entityNameToEntity.TryAdd(entityNames[i], entity);
                entityNameToDatabaseType.TryAdd(entityNames[i], DatabaseType.MSSQL);

            }

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(entityNameToEntity),
                entityPermissionsMap: entityPermissionsMap,
                IsMultipleCreateOperationEnabled: true);

            ObjectTypeDefinitionNode mutation = GetMutationNode(mutationRoot);
            Assert.IsNotNull(mutation);

            // The permissions are setup for create, update and delete operations.
            // So create, update and delete mutations should get generated.
            // A Check to validate that the count of mutations generated is 4 -
            // 1. 2 Create mutations (point/many) when db supports nested created, else 1.
            // 2. 1 Update mutation
            // 3. 1 Delete mutation
            int totalExpectedMutations = 4 * entityNames.Length;
            Assert.AreEqual(totalExpectedMutations, mutation.Fields.Count);

            for (int i = 0; i < entityNames.Length; i++)
            {
                // Name and Description validations for Create mutation
                string expectedCreateMutationName = $"create{expectedNames[i]}";
                string expectedCreateMutationDescription = $"Creates a new {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedCreateMutationName));
                FieldDefinitionNode createMutation = mutation.Fields.First(f => f.Name.Value == expectedCreateMutationName);
                Assert.AreEqual(expectedCreateMutationDescription, createMutation.Description.Value);

                // Name and Description validations for CreateMultiple mutation
                string expectedCreateMultipleMutationName = $"create{expectedCreateMultipleMutationNames[i]}";
                string expectedCreateMultipleMutationDescription = $"Creates multiple new {expectedCreateMultipleMutationNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedCreateMultipleMutationName));
                FieldDefinitionNode createMultipleMutation = mutation.Fields.First(f => f.Name.Value == expectedCreateMultipleMutationName);
                Assert.AreEqual(expectedCreateMultipleMutationDescription, createMultipleMutation.Description.Value);

                // Name and Description validations for Update mutation
                string expectedUpdateMutationName = $"update{expectedNames[i]}";
                string expectedUpdateMutationDescription = $"Updates a {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedUpdateMutationName));
                FieldDefinitionNode updateMutation = mutation.Fields.First(f => f.Name.Value == expectedUpdateMutationName);
                Assert.AreEqual(expectedUpdateMutationDescription, updateMutation.Description.Value);

                // Name and Description validations for Delete mutation
                string expectedDeleteMutationName = $"delete{expectedNames[i]}";
                string expectedDeleteMutationDescription = $"Delete a {expectedNames[i]}";
                Assert.AreEqual(1, mutation.Fields.Count(f => f.Name.Value == expectedDeleteMutationName));
                FieldDefinitionNode deleteMutation = mutation.Fields.First(f => f.Name.Value == expectedDeleteMutationName);
                Assert.AreEqual(expectedDeleteMutationDescription, deleteMutation.Description.Value);
            }
        }

        /// <summary>
        /// Tests the GraphQL schema builder method MutationBuilder.Build()'s behavior when processing stored procedure entity configuration
        /// which may explicitly define the field type(query/mutation) of the entity.
        /// Only attempt to get the mutation ObjectTypeDefinitionNode when a mutation root operation type exists.
        /// In this test, either a mutation is created or it is not, so only attempt to run GetMutationNode when a node is expected,
        /// otherwise an exception is raised.
        /// This is expected per GraphQL Specification (Oct 2021) https://spec.graphql.org/October2021/#sec-Root-Operation-Types
        /// "The mutation root operation type is optional; if it is not provided, the service does not support mutations."
        /// </summary>
        /// <param name="graphQLOperation">Query or Mutation</param>
        /// <param name="operations">Collection of operations denoted by their enum value, for CreateStubEntityPermissionsMap() </param>
        /// <param name="permissionOperations">Collection of operations denoted by their string value, for GenerateStoredProcedureEntity()</param>
        /// <param name="expectsMutationField">Whether MutationBuilder will generate a mutation field for the GraphQL schema.</param>
        [DataTestMethod]
        [DataRow(GraphQLOperation.Mutation, new[] { EntityActionOperation.Execute }, new[] { "execute" }, true, DisplayName = "Mutation field generated since all metadata is valid")]
        [DataRow(null, new[] { EntityActionOperation.Execute }, new[] { "execute" }, true, DisplayName = "Mutation field generated since default operation is mutation.")]
        [DataRow(GraphQLOperation.Mutation, new[] { EntityActionOperation.Read }, new[] { "read" }, false, DisplayName = "Mutation field not generated because invalid permissions were supplied")]
        [DataRow(GraphQLOperation.Query, new[] { EntityActionOperation.Execute }, new[] { "execute" }, false, DisplayName = "Mutation field not generated because the configured operation is query.")]
        public void StoredProcedureEntityAsMutationField(GraphQLOperation? graphQLOperation, EntityActionOperation[] operations, string[] permissionOperations, bool expectsMutationField)
        {
            string entityName = "MyStoredProcedure";
            string gql =
            @"
            type StoredProcedureType @model(name:""MyStoredProcedure"") {
                field1: string
            }
            ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            _entityPermissions = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { entityName },
                    operations,
                    new string[] { "anonymous", "authenticated" }
                );
            Entity entity = GraphQLTestHelpers.GenerateStoredProcedureEntity(graphQLTypeName: "StoredProcedureType", graphQLOperation, permissionOperations);

            DatabaseObject spDbObj = new DatabaseStoredProcedure(schemaName: "dbo", tableName: "dbObjectName")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new()
                {
                    Parameters = new() {
                        { "field1", new() { SystemType = typeof(string) } }
                    }
                }
            };

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { entityName, DatabaseType.MSSQL }
            };

            DocumentNode mutationRoot = MutationBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { entityName, entity } }),
                entityPermissionsMap: _entityPermissions,
                dbObjects: new Dictionary<string, DatabaseObject> { { entityName, spDbObj } }
            );

            const string FIELDNOTFOUND_ERROR = "The expected mutation field schema was not detected.";
            try
            {
                // Gets the specific mutation field generated for GraphQL type 'StoredProcedureType' named 'executeMyStoredProcedure'
                // from the schema root field 'Mutation'
                ObjectTypeDefinitionNode mutation = GetMutationNode(mutationRoot);

                // With a minimized configuration for this entity, the only field expected is the one that may be generated from this test.
                Assert.IsTrue(mutation.Fields.Any(), message: FIELDNOTFOUND_ERROR);
                FieldDefinitionNode field = mutation.Fields.First(f => f.Name.Value == $"executeStoredProcedureType");
                Assert.IsNotNull(field, message: FIELDNOTFOUND_ERROR);
                string actualMutationType = field.Type.ToString();
                Assert.AreEqual(expected: "[StoredProcedureType!]!", actual: actualMutationType, message: $"Incorrect mutation field type: {actualMutationType}");
            }
            catch (Exception ex)
            {
                if (expectsMutationField)
                {
                    Assert.Fail(message: $"{FIELDNOTFOUND_ERROR} {ex.Message}");
                }
                else
                {
                    Assert.IsTrue(mutationRoot.Definitions.Count == 0, message: FIELDNOTFOUND_ERROR);
                }
            }
        }
    }
}
