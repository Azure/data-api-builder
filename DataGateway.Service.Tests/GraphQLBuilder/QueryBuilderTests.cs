using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class QueryBuilderTests
    {
        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Single item access")]
        public void CanGenerateByPKQuery()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"Foo_by_pk"));
        }

        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Single item access")]
        public void UsedIdFieldByDefault()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"Foo_by_pk");
            IReadOnlyList<InputValueDefinitionNode> args = field.Arguments;

            Assert.AreEqual(1, args.Count);
            Assert.IsTrue(args.All(a => a.Name.Value == "id"));
            Assert.AreEqual("ID", args.First(a => a.Name.Value == "id").Type.InnerType().NamedType().Name.Value);
            Assert.IsTrue(args.First(a => a.Name.Value == "id").Type.IsNonNullType());
        }

        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Collection access")]
        public void CanGenerateCollectionQuery()
        {
            string gql =
                @"
type Foo @model {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(root);

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"Foos"));
        }

        private static ObjectTypeDefinitionNode GetQueryNode(DocumentNode queryRoot)
        {
            return (ObjectTypeDefinitionNode)queryRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Query");
        }
    }
}
