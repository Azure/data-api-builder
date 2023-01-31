using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.GraphQLBuilder
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
                    new Config.Operation[] { Config.Operation.Read },
                    new string[] { "anonymous", "authenticated" }
                    );
        }

        [DataTestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Single item access")]
        [DataRow(new string[] { "authenticated" }, true,
            DisplayName = "Validates @authorize directive is added.")]
        [DataRow(new string[] { "anonymous" }, false,
            DisplayName = "Validates @authorize directive is NOT added.")]
        [DataRow(new string[] { "anonymous", "authenticated" }, false,
            DisplayName = "Validates @authorize directive is NOT added - multiple roles")]
        public void CanGenerateByPKQuery(
            IEnumerable<string> roles,
            bool isAuthorizeDirectiveExpected)
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "Foo" },
                    new Config.Operation[] { Config.Operation.Read },
                    roles);
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.cosmosdb_nosql,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"foo_by_pk"));
            FieldDefinitionNode pkField =
                query.Fields.Where(f => f.Name.Value == $"foo_by_pk").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: pkField.Directives.Count);
        }

        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Single item access")]
        public void UsedIdFieldByDefault()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.cosmosdb_nosql,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"foo_by_pk");
            IReadOnlyList<InputValueDefinitionNode> args = field.Arguments;

            Assert.AreEqual(2, args.Count);
            Assert.IsTrue(args.Any(a => a.Name.Value == "id"));
            Assert.AreEqual("ID", args.First(a => a.Name.Value == "id").Type.InnerType().NamedType().Name.Value);
            Assert.IsTrue(args.Any(a => a.Name.Value == "_partitionKeyValue"));
            Assert.AreEqual("String", args.First(a => a.Name.Value == "_partitionKeyValue").Type.InnerType().NamedType().Name.Value);
        }

        [DataTestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Collection access")]
        [DataRow(new string[] { "authenticated" }, true,
            DisplayName = "Validates @authorize directive is added.")]
        [DataRow(new string[] { "anonymous" }, false,
            DisplayName = "Validates @authorize directive is NOT added.")]
        [DataRow(new string[] { "anonymous", "authenticated" }, false,
            DisplayName = "Validates @authorize directive is NOT added - multiple roles")]
        public void CanGenerateCollectionQuery(
            IEnumerable<string> roles,
            bool isAuthorizeDirectiveExpected)
        {
            string gql =
                @"
type foo @model(name:""foo"") {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "foo" },
                    new Config.Operation[] { Config.Operation.Read },
                    roles);
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.cosmosdb_nosql,
                new Dictionary<string, Entity> { { "foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == $"foos"));
            FieldDefinitionNode collectionField =
                query.Fields.Where(f => f.Name.Value == $"foos").First();
            Assert.AreEqual(expected: isAuthorizeDirectiveExpected ? 1 : 0,
                actual: collectionField.Directives.Count);
        }

        [TestMethod]
        [TestCategory("Query Generation")]
        [TestCategory("Collection access")]
        public void CollectionQueryResultTypeHasItemFieldAndContinuation()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    id: ID!
}
                ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.cosmosdb_nosql,
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
type Foo @model(name:""Foo"") {
    foo_id: Int! @primaryKey(databaseType: ""bigint"")
}
";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.mssql,
                new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            FieldDefinitionNode byIdQuery = query.Fields.First(f => f.Name.Value == $"foo_by_pk");
            Assert.AreEqual("foo_id", byIdQuery.Arguments[0].Name.Value);
        }

        [TestMethod]
        public void PrimaryKeyFieldAsQueryInputCosmos()
        {
            string gql =
                @"
type Foo @model(name:""Foo"") {
    foo_id: Int!
}
";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);

            DocumentNode queryRoot = QueryBuilder.Build(
                            root,
                            DatabaseType.cosmosdb_nosql,
                            new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } },
                            inputTypes: new(),
                            entityPermissionsMap: _entityPermissions
                            );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            FieldDefinitionNode byIdQuery = query.Fields.First(f => f.Name.Value == $"foo_by_pk");
            Assert.AreEqual("id", byIdQuery.Arguments[0].Name.Value);
            Assert.AreEqual("_partitionKeyValue", byIdQuery.Arguments[1].Name.Value);
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

        /// <summary>
        /// We assume that the user will provide a singular name for the entity. Users have the option of providing singular and
        /// plural names for an entity in the config to have more control over the graphql schema generation.
        /// When singular and plural names are specified by the user, these names will be used for generating the
        /// queries and mutations in the schema.
        /// When singular and plural names are not provided, the queries and mutations will be generated with the entity's name.
        /// Further, the queries and descriptions will get generated with the same case as defined by the user.
        /// This test validates that this naming convention is followed for the queries when the schema is generated.
        /// </summary>
        /// <param name="gql">Type definition for the entity</param>
        /// <param name="entityName">Name of the entity</param>
        /// <param name="singularName">Singular name provided by the user</param>
        /// <param name="pluralName">Plural name provided by the user</param>
        /// <param name="expectedQueryNameForPK"> Expected name for the primary key query</param>
        /// <param name="expectedQueryNameForList"> Expected name for the query to fetch all items </param>
        /// <param name="expectedNameInDescription">Expected name in the description for both the queries</param>
        [DataTestMethod]
        [DataRow(GraphQLTestHelpers.BOOK_GQL, "Book", null, null, "book_by_pk", "books", "Book",
            DisplayName = "Query name and description validation for singular entity name with singular plural not defined")]
        [DataRow(GraphQLTestHelpers.BOOK_GQL, "Book", "book", "books", "book_by_pk", "books", "book",
            DisplayName = "Query name and description validation for singular entity name with singular plural defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, "Books", null, null, "books_by_pk", "books", "Books",
            DisplayName = "Query name and description validation for plural entity name with singular plural not defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, "Books", "book", "books", "book_by_pk", "books", "book",
            DisplayName = "Query name and description validation for plural entity name with singular plural defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, "Books", "book", "", "book_by_pk", "books", "book",
            DisplayName = "Query name and description validations for plural entity name with singular defined")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, "People", "Person", "People", "person_by_pk", "people", "Person",
            DisplayName = "Query name and description validation for indirect plural entity name with singular and plural name defined")]
        public void ValidateQueriesAreCreatedWithRightName(
            string gql,
            string entityName,
            string singularName,
            string pluralName,
            string expectedQueryNameForPK,
            string expectedQueryNameForList,
            string expectedNameInDescription
            )
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { entityName },
                    new Config.Operation[] { Config.Operation.Read },
                    new string[] { "anonymous", "authenticated" });

            Entity entity = (singularName is not null)
                                ? GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularName, pluralName)
                                : GraphQLTestHelpers.GenerateEmptyEntity();

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.cosmosdb_nosql,
                new Dictionary<string, Entity> { { entityName, entity } },
                inputTypes: new(),
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.IsNotNull(query);

            // Two queries - 1) Query for an item using PK 2) Query for all items should be created.
            // Check to validate the count of queries created.
            Assert.AreEqual(2, query.Fields.Count);

            // Name and Description validations for the query for fetching by PK.
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == expectedQueryNameForPK));
            FieldDefinitionNode pkQueryFieldNode = query.Fields.First(f => f.Name.Value == expectedQueryNameForPK);
            string expectedPKQueryDescription = $"Get a {expectedNameInDescription} from the database by its ID/primary key";
            Assert.AreEqual(expectedPKQueryDescription, pkQueryFieldNode.Description.Value);

            // Name and Description validations for the query for fetching all items.
            Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == expectedQueryNameForList));
            FieldDefinitionNode allItemsQueryFieldNode = query.Fields.First(f => f.Name.Value == expectedQueryNameForList);
            string expectedAllQueryDescription = $"Get a list of all the {expectedNameInDescription} items from the database";
            Assert.AreEqual(expectedAllQueryDescription, allItemsQueryFieldNode.Description.Value);
        }

        /// <summary>
        /// Tests the GraphQL schema builder method QueryBuild.Build()'s behavior when processing stored procedure entity configuration
        /// which may explicitly define the field type(query/mutation) of the entity.
        /// </summary>
        /// <param name="graphQLOperation">Query or Mutation</param>
        /// <param name="operations">CRUD + Execute -> for EntityPermissionsMap </param>
        /// <param name="permissionOperations">CRUD + Execute -> for Entity.Permissions</param>
        /// <param name="expectsQueryField">Whether QueryBuilder will generate a query field for the GraphQL schema.</param>
        [DataTestMethod]
        [Ignore]
        [DataRow(GraphQLOperation.Query, new[] { Config.Operation.Execute }, new[] { "execute" }, true, DisplayName = "Query field generated since all metadata is valid")]
        [DataRow(null, new[] { Config.Operation.Execute }, new[] { "execute" }, false, DisplayName = "Query field not generated since default operation is mutation.")]
        [DataRow(GraphQLOperation.Query, new[] { Config.Operation.Read }, new[] { "read" }, false, DisplayName = "Query field not generated because invalid permissions were supplied")]
        [DataRow(GraphQLOperation.Mutation, new[] { Config.Operation.Execute }, new[] { "execute" }, false, DisplayName = "Query field not generated because the configured operation is mutation.")]
        public void StoredProcedureEntityAsQueryField(GraphQLOperation graphQLOperation, Config.Operation[] operations, string[] permissionOperations, bool expectsQueryField)
        {
            string gql =
            @"
            type StoredProcedureType @model(name:""MyStoredProcedure"") {
                field1: string
            }
            ";

            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            _entityPermissions = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    new string[] { "MyStoredProcedure" },
                    operations,
                    new string[] { "anonymous", "authenticated" }
                    );
            Entity entity = GraphQLTestHelpers.GenerateStoredProcedureEntity(graphQLTypeName: "StoredProcedureType", graphQLOperation, permissionOperations);

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                DatabaseType.mssql,
                new Dictionary<string, Entity> { { "MyStoredProcedure", entity } },
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);

            // With a minimized configuration for this entity, the only field expected is the one that may be generated from this test.
            const string FIELDNOTFOUND_ERROR = "The expected query field definition was not detected.";

            if (expectsQueryField)
            {
                Assert.IsTrue(query.Fields.Any(), message: FIELDNOTFOUND_ERROR);
                FieldDefinitionNode field = query.Fields.First(f => f.Name.Value == $"executeStoredProcedureType");
                Assert.IsNotNull(field, message: FIELDNOTFOUND_ERROR);
                string actualQueryType = field.Type.ToString();
                Assert.AreEqual(expected: "[StoredProcedureType!]!", actual: actualQueryType, message: $"Incorrect query field type: {actualQueryType}");
            }
            else
            {
                Assert.IsTrue(!query.Fields.Any(), message: FIELDNOTFOUND_ERROR);
            }

        }

        public static ObjectTypeDefinitionNode GetQueryNode(DocumentNode queryRoot)
        {
            return (ObjectTypeDefinitionNode)queryRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Query");
        }
    }
}
