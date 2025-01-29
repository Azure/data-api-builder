// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
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
                    new EntityActionOperation[] { EntityActionOperation.Read },
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
                    new EntityActionOperation[] { EntityActionOperation.Read },
                    roles);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
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
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
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
                    new EntityActionOperation[] { EntityActionOperation.Read },
                    roles);
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
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
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            string returnTypeName = query.Fields.First(f => f.Name.Value == $"foos").Type.NamedType().Name.Value;
            ObjectTypeDefinitionNode returnType = queryRoot.Definitions.Where(d => d is ObjectTypeDefinitionNode).Cast<ObjectTypeDefinitionNode>().First(d => d.Name.Value == returnTypeName);

            // Verify items field exists and has correct type
            FieldDefinitionNode itemsField = returnType.Fields.FirstOrDefault(f => f.Name.Value == "items");
            Assert.IsNotNull(itemsField, "items field should exist");
            Assert.AreEqual("[Foo!]!", itemsField.Type.ToString(), "items field should be non-null list of non-null Foo");

            // Verify pagination token field exists and has correct type
            FieldDefinitionNode paginationTokenField = returnType.Fields.FirstOrDefault(f => f.Name.Value == QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
            Assert.IsNotNull(paginationTokenField, "pagination token field should exist");
            Assert.AreEqual("String", paginationTokenField.Type.NamedType().Name.Value, "pagination token should be String type");

            // Verify hasNextPage field exists and has correct type
            FieldDefinitionNode hasNextPageField = returnType.Fields.FirstOrDefault(f => f.Name.Value == "hasNextPage");
            Assert.IsNotNull(hasNextPageField, "hasNextPage field should exist");
            Assert.AreEqual("Boolean", hasNextPageField.Type.NamedType().Name.Value, "hasNextPage should be Boolean type");
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

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.MSSQL }
            };

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
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
            Dictionary<string, DatabaseType> entityNameToDatabaseType = new()
            {
                { "Foo", DatabaseType.CosmosDB_NoSQL }
            };
            DocumentNode queryRoot = QueryBuilder.Build(
                            root,
                            entityNameToDatabaseType,
                            new(new Dictionary<string, Entity> { { "Foo", GraphQLTestHelpers.GenerateEmptyEntity() } }),
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
        public void RelationshipWithCardinalityOfOneIsNotUpdated()
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
        /// <param name="entityNames">Names of the entities</param>
        /// <param name="singularNames">Singular name provided by the user</param>
        /// <param name="pluralNames">Plural names provided by the user</param>
        /// <param name="expectedQueryNamesForPK"> Expected names for the primary key query</param>
        /// <param name="expectedQueryNamesForList"> Expected names for the query to fetch all items </param>
        /// <param name="expectedNameInDescription">Expected names in the description for both the queries</param>
        [DataTestMethod]
        [DataRow(GraphQLTestHelpers.BOOK_GQL, new string[] { "Book" }, null, null, new string[] { "book_by_pk" }, new string[] { "books" }, new string[] { "Book" },
            DisplayName = "Query name and description validation for singular entity name with singular plural not defined")]
        [DataRow(GraphQLTestHelpers.BOOK_GQL, new string[] { "Book" }, new string[] { "book" }, new string[] { "books" }, new string[] { "book_by_pk" }, new string[] { "books" }, new string[] { "book" },
            DisplayName = "Query name and description validation for singular entity name with singular plural defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, new string[] { "Books" }, null, null, new string[] { "books_by_pk" }, new string[] { "books" }, new string[] { "Books" },
            DisplayName = "Query name and description validation for plural entity name with singular plural not defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, new string[] { "Books" }, new string[] { "book" }, new string[] { "books" }, new string[] { "book_by_pk" }, new string[] { "books" }, new string[] { "book" },
            DisplayName = "Query name and description validation for plural entity name with singular plural defined")]
        [DataRow(GraphQLTestHelpers.BOOKS_GQL, new string[] { "Books" }, new string[] { "book" }, new string[] { "" }, new string[] { "book_by_pk" }, new string[] { "books" }, new string[] { "book" },
            DisplayName = "Query name and description validations for plural entity name with singular defined")]
        [DataRow(GraphQLTestHelpers.PEOPLE_GQL, new string[] { "People" }, new string[] { "Person" }, new string[] { "People" }, new string[] { "person_by_pk" }, new string[] { "people" }, new string[] { "Person" },
            DisplayName = "Query name and description validation for indirect plural entity name with singular and plural name defined")]
        [DataRow($"{GraphQLTestHelpers.BOOK_GQL}{GraphQLTestHelpers.PEOPLE_GQL}", new string[] { "Book", "People" }, null, null, new string[] { "book_by_pk", "people_by_pk" }, new string[] { "books", "peoples" }, new string[] { "Book", "People" },
            DisplayName = "Query name and description validation for singular entity name with singular plural not defined")]
        [DataRow($"{GraphQLTestHelpers.BOOK_GQL}{GraphQLTestHelpers.PEOPLE_GQL}", new string[] { "Book", "People" }, new string[] { "book", "Person" }, new string[] { "books", "People" }, new string[] { "book_by_pk", "person_by_pk" }, new string[] { "books", "people" }, new string[] { "book", "Person" },
            DisplayName = "Query name and description validation for single and plural names defined not defined")]
        public void ValidateQueriesAreCreatedWithRightName(
            string gql,
            string[] entityNames,
            string[] singularNames,
            string[] pluralNames,
            string[] expectedQueryNamesForPK,
            string[] expectedQueryNamesForList,
            string[] expectedNameInDescription
            )
        {
            DocumentNode root = Utf8GraphQLParser.Parse(gql);
            Dictionary<string, EntityMetadata> entityPermissionsMap
                = GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames,
                    new EntityActionOperation[] { EntityActionOperation.Read },
                    new string[] { "anonymous", "authenticated" });

            Dictionary<string, DatabaseType> entityNameToDatabaseType = new();
            Dictionary<string, Entity> entityNameToEntity = new();

            for (int i = 0; i < entityNames.Length; i++)
            {
                Entity entity = (singularNames is not null)
                    ? GraphQLTestHelpers.GenerateEntityWithSingularPlural(singularNames[i], pluralNames[i])
                    : GraphQLTestHelpers.GenerateEmptyEntity();
                entityNameToEntity.TryAdd(entityNames[i], entity);
                entityNameToDatabaseType.TryAdd(entityNames[i], i % 2 == 0 ? DatabaseType.CosmosDB_NoSQL : DatabaseType.MSSQL);
            }

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(entityNameToEntity),
                inputTypes: new(),
                entityPermissionsMap: entityPermissionsMap
                );

            ObjectTypeDefinitionNode query = GetQueryNode(queryRoot);
            Assert.IsNotNull(query);

            // Two queries - 1) Query for an item using PK 2) Query for all items should be created.
            // Check to validate the count of queries created.
            Assert.AreEqual(2 * entityNames.Length, query.Fields.Count);

            for (int i = 0; i < entityNames.Length; i++)
            {
                // Name and Description validations for the query for fetching by PK.
                Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == expectedQueryNamesForPK[i]));
                FieldDefinitionNode pkQueryFieldNode = query.Fields.First(f => f.Name.Value == expectedQueryNamesForPK[i]);
                string expectedPKQueryDescription = $"Get a {expectedNameInDescription[i]} from the database by its ID/primary key";
                Assert.AreEqual(expectedPKQueryDescription, pkQueryFieldNode.Description.Value);

                // Name and Description validations for the query for fetching all items.
                Assert.AreEqual(1, query.Fields.Count(f => f.Name.Value == expectedQueryNamesForList[i]));
                FieldDefinitionNode allItemsQueryFieldNode = query.Fields.First(f => f.Name.Value == expectedQueryNamesForList[i]);
                string expectedAllQueryDescription = $"Get a list of all the {expectedNameInDescription[i]} items from the database";
                Assert.AreEqual(expectedAllQueryDescription, allItemsQueryFieldNode.Description.Value);
            }
        }

        /// <summary>
        /// Tests the GraphQL schema builder method QueryBuilder.Build()'s behavior when processing stored procedure entity configuration
        /// which may explicitly define the field type(query/mutation) of the entity.
        /// </summary>
        /// <param name="graphQLOperation">Query or Mutation</param>
        /// <param name="operations">CRUD + Execute -> for EntityPermissionsMap </param>
        /// <param name="permissionOperations">CRUD + Execute -> for Entity.Permissions</param>
        /// <param name="expectsQueryField">Whether QueryBuilder will generate a query field for the GraphQL schema.</param>
        [DataTestMethod]
        [DataRow(GraphQLOperation.Query, new[] { EntityActionOperation.Execute }, new[] { "execute" }, true, DisplayName = "Query field generated since all metadata is valid")]
        [DataRow(null, new[] { EntityActionOperation.Execute }, new[] { "execute" }, false, DisplayName = "Query field not generated since default operation is mutation.")]
        [DataRow(GraphQLOperation.Query, new[] { EntityActionOperation.Read }, new[] { "read" }, false, DisplayName = "Query field not generated because invalid permissions were supplied")]
        [DataRow(GraphQLOperation.Mutation, new[] { EntityActionOperation.Execute }, new[] { "execute" }, false, DisplayName = "Query field not generated because the configured operation is mutation.")]
        public void StoredProcedureEntityAsQueryField(GraphQLOperation? graphQLOperation, EntityActionOperation[] operations, string[] permissionOperations, bool expectsQueryField)
        {
            string entityName = "MyStoredProcedure";
            string gql =
            @"
            type StoredProcedureType @model(name:" + entityName + @") {
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

            DocumentNode queryRoot = QueryBuilder.Build(
                root,
                entityNameToDatabaseType,
                new(new Dictionary<string, Entity> { { entityName, entity } }),
                inputTypes: new(),
                entityPermissionsMap: _entityPermissions,
                dbObjects: new Dictionary<string, DatabaseObject> { { entityName, spDbObj } }
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

        /// <summary>
        /// Tests that the return type includes a properly structured groupBy field.
        /// Verifies:
        /// 1. groupBy field exists with correct structure
        /// 2. fields argument is a non-null list of non-null scalar field enum values
        /// 3. groupBy field returns a non-null list of non-null GroupBy type
        /// </summary>
        [TestMethod]
        [TestCategory("Query Builder - Return Type")]
        public void GenerateReturnType_IncludesGroupByField()
        {
            // Arrange
            NameNode entityName = new("Book");

            // Act
            ObjectTypeDefinitionNode returnType = QueryBuilder.GenerateReturnType(entityName, isAggregationEnabled: true);

            // Assert
            FieldDefinitionNode groupByField = returnType.Fields.FirstOrDefault(f => f.Name.Value == "groupBy");
            Assert.IsNotNull(groupByField, "groupBy field should exist");

            // Verify fields argument
            InputValueDefinitionNode fieldsArg = groupByField.Arguments.FirstOrDefault(a => a.Name.Value == "fields");
            Assert.IsNotNull(fieldsArg, "fields argument should exist");

            // Check that fields argument is [BookScalarFields!]
            ListTypeNode listType = fieldsArg.Type as ListTypeNode;
            Assert.IsNotNull(listType, "fields argument should be a list");
            Assert.IsTrue(listType.Type is NonNullTypeNode, "list elements should be non-null");
            NamedTypeNode enumType = (listType.Type as NonNullTypeNode)!.Type as NamedTypeNode;
            Assert.IsNotNull(enumType, "list elements should be named type");
            Assert.AreEqual("BookScalarFields", enumType.Name.Value, "should use scalar fields enum");

            // Check return type is [BookGroupBy!]!
            Assert.IsTrue(groupByField.Type is NonNullTypeNode, "return type should be non-null");
            ListTypeNode returnListType = (groupByField.Type as NonNullTypeNode)!.Type as ListTypeNode;
            Assert.IsNotNull(returnListType, "return type should be a list");
            Assert.IsTrue(returnListType.Type is NonNullTypeNode, "return list elements should be non-null");
            NamedTypeNode groupByType = (returnListType.Type as NonNullTypeNode)!.Type as NamedTypeNode;
            Assert.IsNotNull(groupByType, "return elements should be named type");
            Assert.AreEqual("BookGroupBy", groupByType.Name.Value, "should return GroupBy type");
        }

        public static ObjectTypeDefinitionNode GetQueryNode(DocumentNode queryRoot)
        {
            return (ObjectTypeDefinitionNode)queryRoot.Definitions.First(d => d is ObjectTypeDefinitionNode node && node.Name.Value == "Query");
        }
    }
}
