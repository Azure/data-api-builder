using System.Linq;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class MutationBuilderTests
    {
        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CanGenerateCreateMutationWith_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Simple Type")]
        public void CreateMutationExcludeIdFromInput_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            Assert.AreEqual(1, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[0].Type.NamedType().Name);
            Assert.AreEqual(1, argType.Fields.Count);
            Assert.AreEqual("bar", argType.Fields[0].Name.Value);
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Complex Type")]
        public void CreateMutationExcludeIdFromInput_ComplexType()
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(2, inputObj.Fields.Count);
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"createFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Create")]
        [TestCategory("Schema Builder - Nested Type")]
        public void CreateMutationExcludeIdFromInput_NestedType()
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"createFoo");
            InputValueDefinitionNode inputArg = field.Arguments[0];
            InputObjectTypeDefinitionNode inputObj = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is InputObjectTypeDefinitionNode node && node.Name == inputArg.Type.NamedType().Name);
            Assert.AreEqual(1, inputObj.Fields.Count);
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"deleteFoo"));
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Delete")]
        [TestCategory("Schema Builder - Simple Type")]
        public void DeleteMutationIdAsInput_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"deleteFoo");
            Assert.AreEqual(1, field.Arguments.Count);
            Assert.AreEqual("id", field.Arguments[0].Name.Value);
            Assert.AreEqual("ID", field.Arguments[0].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[0].Type.IsNonNullType());
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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

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

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            Assert.AreEqual(2, field.Arguments.Count);

            InputObjectTypeDefinitionNode argType = (InputObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is INamedSyntaxNode node && node.Name == field.Arguments[1].Type.NamedType().Name);
            Assert.AreEqual(1, argType.Fields.Count);
            Assert.AreEqual("bar", argType.Fields[0].Name.Value);
        }

        [TestMethod]
        [TestCategory("Mutation Builder - Update")]
        [TestCategory("Schema Builder - Simple Type")]
        public void UpdateMutationIdFieldAsArgument_SimpleType()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
    bar: String!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode mutationRoot = MutationBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetMutationNode(mutationRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"updateFoo");
            Assert.AreEqual(2, field.Arguments.Count);
            Assert.AreEqual("id", field.Arguments[0].Name.Value);
            Assert.AreEqual("ID", field.Arguments[0].Type.NamedType().Name.Value);
            Assert.IsTrue(field.Arguments[0].Type.IsNonNullType());
        }

        private static ObjectTypeDefinitionNode GetMutationNode(DocumentNode mutationRoot)
        {
            return (ObjectTypeDefinitionNode)mutationRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Mutation");

        }
    }
}
