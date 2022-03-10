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
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"foo_by_pk"));
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
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"foo_by_pk");
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
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"foos"));
        }

        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Collection access")]
        public void CollectionQueryResultTypeHasItemFieldAndContinuation()
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
            string returnTypeName = query.Fields.First(f => f.Name.Value == $"foos").Type.NamedType().Name.Value;
            ObjectTypeDefinitionNode returnType = queryRoot.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>().First(d => d.Name.Value == returnTypeName);
            Assert.AreEqual(2, returnType.Fields.Count);
            Assert.AreEqual("items", returnType.Fields[0].Name.Value);
            Assert.AreEqual("[Foo!]!", returnType.Fields[0].Type.ToString());
            Assert.AreEqual("continuation", returnType.Fields[1].Name.Value);
            Assert.AreEqual("String", returnType.Fields[1].Type.NamedType().Name.Value);
        }

        private static ObjectTypeDefinitionNode GetQueryNode(DocumentNode queryRoot)
        {
            return (ObjectTypeDefinitionNode)queryRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Query");
        }
    }
}
