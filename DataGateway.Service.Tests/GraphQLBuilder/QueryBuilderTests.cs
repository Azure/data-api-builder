using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.GraphQLBuilder
{
    [TestClass]
    public class QueryBuilderTests
    {
        private const int NUMBER_OF_ARGUMENTS = 4;

        private Dictionary<string, EntityMetadata> _entityPermissions;

        /// <summary>
        /// Create stub entityPermissions to enable QueryBuilder to create
        /// queries in GraphQL schema. Without permissions present
        /// (i.e. no roles defined for action on entity), then queries
        /// will not be created in schema since it is inaccessible
        /// as stated by permissions configuration.
        /// </summary>
        [TestInitialize]
        public void SetupEntityPermissionsMap()
        {
            _entityPermissions = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new string[] { ActionType.READ },
                    new string[] { "anonymous", "authenticated" }
                    );
        }

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

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

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

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

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

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

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

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            string returnTypeName = query.Fields.First(f => f.Name.Value == $"foos").Type.NamedType().Name.Value;
            ObjectTypeDefinitionNode returnType = queryRoot.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>().First(d => d.Name.Value == returnTypeName);
            Assert.AreEqual(3, returnType.Fields.Count);
            Assert.AreEqual("items", returnType.Fields[0].Name.Value);
            Assert.AreEqual("[Foo!]!", returnType.Fields[0].Type.ToString());
            Assert.AreEqual(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME, returnType.Fields[1].Name.Value);
            Assert.AreEqual("String", returnType.Fields[1].Type.NamedType().Name.Value);
            Assert.AreEqual("hasNextPage", returnType.Fields[2].Name.Value);
            Assert.AreEqual("Boolean", returnType.Fields[2].Type.NamedType().Name.Value);
        }

        [TestMethod]
        public void PrimaryKeyFieldAsQueryInput()
        {
            string gql =
                @"
type Foo @model {
    foo_id: Int! @primaryKey(databaseType: ""bigint"")
}
";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            FieldDefinitionNode byIdQuery = query.Fields.First(f => f.Name.Value == $"foo_by_pk");
            Assert.AreEqual("foo_id", byIdQuery.Arguments[0].Name.Value);
        }

        [TestMethod]
        public void RelationshipWithCardinalityOfManyGetsQueryFields()
        {
            string gql =
                @"
type Table @model(name: ""table"") {
  otherTable: FkTable! @relationship(target: ""FkTable"", cardinality: ""Many"")
}
";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = (ObjectTypeDefinitionNode)root.Definitions[0];

            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new()
            {
                { "FkTable", new InputObjectTypeDefinitionNode(location: null, new("FkTableFilter"), description: null, new List<DirectiveNode>(), new List<InputValueDefinitionNode>()) }
            };
            ObjectTypeDefinitionNode updatedNode = QueryBuilder.AddQueryArgumentsForRelationships(node, inputObjects);

            Assert.AreNotEqual(node, updatedNode);

            FieldDefinitionNode field = updatedNode.Fields[0];
            Assert.AreEqual(NUMBER_OF_ARGUMENTS, field.Arguments.Count, $"Query fields should have {NUMBER_OF_ARGUMENTS} arguments");
            Assert.AreEqual(QueryBuilder.PAGE_START_ARGUMENT_NAME, field.Arguments[0].Name.Value, "First argument should be the page start");
            Assert.AreEqual(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME, field.Arguments[1].Name.Value, "Second argument is pagination token");
            Assert.AreEqual(QueryBuilder.FILTER_FIELD_NAME, field.Arguments[2].Name.Value, "Third argument is typed filter field");
            Assert.AreEqual("FkTableFilterInput", field.Arguments[2].Type.NamedType().Name.Value, "Typed filter field should be filter type of target object type");
            Assert.AreEqual(QueryBuilder.ORDER_BY_FIELD_NAME, field.Arguments[3].Name.Value, "Fourth argument is typed order by field");
            Assert.AreEqual("FkTableOrderByInput", field.Arguments[3].Type.NamedType().Name.Value, "Typed order by field should be order by type of target object type");
        }

        [TestMethod]
        public void RelationshipWithCardinalityOfOneIsntUpdated()
        {
            string gql =
                @"
type Table @model(name: ""table"") {
  otherTable: FkTable! @relationship(target: ""FkTable"", cardinality: ""One"")
}
";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            ObjectTypeDefinitionNode node = (ObjectTypeDefinitionNode)root.Definitions[0];

            Dictionary<string, InputObjectTypeDefinitionNode> inputObjects = new()
            {
                { "FkTable", new InputObjectTypeDefinitionNode(location: null, new("FkTableFilter"), description: null, new List<DirectiveNode>(), new List<InputValueDefinitionNode>()) }
            };
            ObjectTypeDefinitionNode updatedNode = QueryBuilder.AddQueryArgumentsForRelationships(node, inputObjects);

            Assert.AreEqual(node, updatedNode);

            FieldDefinitionNode field = updatedNode.Fields[0];
            Assert.AreEqual(0, field.Arguments.Count, "No query fields on cardinality of One relationshop");
        }

        public static ObjectTypeDefinitionNode GetQueryNode(DocumentNode queryRoot)
        {
            return (ObjectTypeDefinitionNode)queryRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Query");
        }
    }
}
