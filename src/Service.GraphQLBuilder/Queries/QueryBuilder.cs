// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using HotChocolate.Language;
using HotChocolate.Types;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLNaming;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLUtils;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.Queries
{
    public static class QueryBuilder
    {
        public const string PAGINATION_FIELD_NAME = "items";
        public const string PAGINATION_TOKEN_FIELD_NAME = "endCursor";
        public const string PAGINATION_TOKEN_ARGUMENT_NAME = "after";
        public const string HAS_NEXT_PAGE_FIELD_NAME = "hasNextPage";
        public const string PAGE_START_ARGUMENT_NAME = "first";
        public const string PAGINATION_OBJECT_TYPE_SUFFIX = "Connection";
        public const string FILTER_FIELD_NAME = "filter";
        public const string ORDER_BY_FIELD_NAME = "orderBy";
        public const string PARTITION_KEY_FIELD_NAME = "_partitionKeyValue";
        public const string ID_FIELD_NAME = "id";
        public const string GROUP_BY_FIELD_NAME = "groupBy";
        public const string GROUP_BY_FIELDS_FIELD_NAME = "fields";
        public const string GROUP_BY_AGGREGATE_FIELD_NAME = "aggregations";
        public const string GROUP_BY_AGGREGATE_FIELD_ARG_NAME = "field";
        public const string GROUP_BY_AGGREGATE_FIELD_DISTINCT_NAME = "distinct";
        public const string GROUP_BY_AGGREGATE_FIELD_HAVING_NAME = "having";

        // Define the enabled database types for aggregation
        public static readonly HashSet<DatabaseType> AggregationEnabledDatabaseTypes = new()
        {
            DatabaseType.MSSQL,
            DatabaseType.DWSQL,
        };

        /// <summary>
        /// Creates a DocumentNode containing FieldDefinitionNodes representing the FindByPK and FindAll queries
        /// Also populates the DocumentNode with return types.
        /// </summary>
        /// <param name="root">Root of GraphQL schema</param>
        /// <param name="databaseTypes">EnitityName to database Type of entity.</param>
        /// <param name="entities">Map of entityName -> EntityMetadata</param>
        /// <param name="entityPermissionsMap">Permissions metadata defined in runtime config.</param>
        /// <param name="dbObjects">Database object metadata</param>
        /// <returns>Queries DocumentNode</returns>
        public static DocumentNode Build(
            DocumentNode root,
            Dictionary<string, DatabaseType> databaseTypes,
            RuntimeEntities entities,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes,
            Dictionary<string, EntityMetadata>? entityPermissionsMap = null,
            Dictionary<string, DatabaseObject>? dbObjects = null,
            bool _isAggregationEnabled = false
            )
        {
            List<FieldDefinitionNode> queryFields = new();
            List<ObjectTypeDefinitionNode> returnTypes = new();

            foreach (IDefinitionNode definition in root.Definitions)
            {
                if (definition is ObjectTypeDefinitionNode objectTypeDefinitionNode && IsModelType(objectTypeDefinitionNode))
                {
                    NameNode name = objectTypeDefinitionNode.Name;
                    string entityName = ObjectTypeToEntityName(objectTypeDefinitionNode);
                    Entity entity = entities[entityName];

                    if (entity.Source.Type is EntitySourceType.StoredProcedure)
                    {
                        // Check runtime configuration of the stored procedure entity to check that the GraphQL operation type was overridden to 'query' from the default 'mutation.'
                        bool isSPDefinedAsQuery = entity.GraphQL.Operation is GraphQLOperation.Query;

                        IEnumerable<string> rolesAllowedForExecute = IAuthorizationResolver.GetRolesForOperation(entityName, operation: EntityActionOperation.Execute, entityPermissionsMap);

                        if (isSPDefinedAsQuery && rolesAllowedForExecute.Any())
                        {
                            if (dbObjects is not null && dbObjects.TryGetValue(entityName, out DatabaseObject? dbObject) && dbObject is not null)
                            {
                                queryFields.Add(GraphQLStoredProcedureBuilder.GenerateStoredProcedureSchema(name, entity, dbObject, rolesAllowedForExecute));
                            }
                        }
                    }
                    else
                    {
                        IEnumerable<string> rolesAllowedForRead = IAuthorizationResolver.GetRolesForOperation(entityName, operation: EntityActionOperation.Read, entityPermissionsMap);
                        bool isAggregationEnabledForEntity = _isAggregationEnabled && AggregationEnabledDatabaseTypes.Contains(databaseTypes[entityName]);

                        ObjectTypeDefinitionNode paginationReturnType = GenerateReturnType(name, isAggregationEnabledForEntity);

                        if (rolesAllowedForRead.Any())
                        {
                            queryFields.Add(GenerateGetAllQuery(objectTypeDefinitionNode, name, paginationReturnType, inputTypes, entity, rolesAllowedForRead));
                            queryFields.Add(GenerateByPKQuery(objectTypeDefinitionNode, name, databaseTypes[entityName], entity, rolesAllowedForRead));
                        }

                        if (paginationReturnType is not null)
                        {
                            returnTypes.Add(paginationReturnType);
                        }
                    }
                }
            }

            List<IDefinitionNode> definitionNodes = new()
            {
                new ObjectTypeDefinitionNode(location: null, new NameNode("Query"), description: null, new List<DirectiveNode>(), new List<NamedTypeNode>(), queryFields),
            };
            definitionNodes.AddRange(returnTypes);
            return new(definitionNodes);
        }

        public static FieldDefinitionNode GenerateByPKQuery(
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            DatabaseType databaseType,
            Entity entity,
            IEnumerable<string>? rolesAllowedForRead = null)
        {
            IEnumerable<FieldDefinitionNode> primaryKeyFields =
            FindPrimaryKeyFields(objectTypeDefinitionNode, databaseType);
            List<InputValueDefinitionNode> inputValues = new();
            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForRead,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            foreach (FieldDefinitionNode primaryKeyField in primaryKeyFields)
            {
                inputValues.Add(new InputValueDefinitionNode(
                    location: null,
                    primaryKeyField.Name,
                    description: null,
                    primaryKeyField.Type,
                    defaultValue: null,
                    new List<DirectiveNode>()));
            }

            return new(
                location: null,
                new NameNode(GenerateByPKQueryName(name.Value, entity)),
                new StringValueNode($"Get a {GetDefinedSingularName(name.Value, entity)} from the database by its ID/primary key"),
                inputValues,
                new NamedTypeNode(name),
                fieldDefinitionNodeDirectives
            );
        }

        public static FieldDefinitionNode GenerateGetAllQuery(
            ObjectTypeDefinitionNode objectTypeDefinitionNode,
            NameNode name,
            ObjectTypeDefinitionNode returnType,
            Dictionary<string, InputObjectTypeDefinitionNode> inputTypes,
            Entity entity,
            IEnumerable<string>? rolesAllowedForRead = null)
        {
            string filterInputName = InputTypeBuilder.GenerateObjectInputFilterName(objectTypeDefinitionNode.Name.Value);

            if (!inputTypes.ContainsKey(filterInputName))
            {
                InputTypeBuilder.GenerateFilterInputTypeForObjectType(objectTypeDefinitionNode, inputTypes);
            }

            string orderByInputName = InputTypeBuilder.GenerateObjectInputOrderByName(objectTypeDefinitionNode.Name.Value);

            if (!inputTypes.ContainsKey(orderByInputName))
            {
                InputTypeBuilder.GenerateOrderByInputTypeForObjectType(objectTypeDefinitionNode, inputTypes);
            }

            List<DirectiveNode> fieldDefinitionNodeDirectives = new();

            if (CreateAuthorizationDirectiveIfNecessary(
                    rolesAllowedForRead,
                    out DirectiveNode? authorizeDirective))
            {
                fieldDefinitionNodeDirectives.Add(authorizeDirective!);
            }

            // Query field for the parent object type
            // Generates a file like:
            //    books(first: Int, after: String, filter: BooksFilterInput, orderBy: BooksOrderByInput): BooksConnection!
            return new(
                location: null,
                new NameNode(GenerateListQueryName(name.Value, entity)),
                new StringValueNode($"Get a list of all the {GetDefinedSingularName(name.Value, entity)} items from the database"),
                QueryArgumentsForField(filterInputName, orderByInputName),
                new NonNullTypeNode(new NamedTypeNode(returnType.Name)),
                fieldDefinitionNodeDirectives
            );
        }

        public static List<InputValueDefinitionNode> QueryArgumentsForField(string filterInputName, string orderByInputName)
        {
            return new()
            {
                new(location: null, new NameNode(PAGE_START_ARGUMENT_NAME), description: new StringValueNode("The number of items to return from the page start point"), new IntType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(PAGINATION_TOKEN_ARGUMENT_NAME), new StringValueNode("A pagination token from a previous query to continue through a paginated list"), new StringType().ToTypeNode(), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(FILTER_FIELD_NAME), new StringValueNode("Filter options for query"), new NamedTypeNode(filterInputName), defaultValue: null, new List<DirectiveNode>()),
                new(location: null, new NameNode(ORDER_BY_FIELD_NAME), new StringValueNode("Ordering options for query"), new NamedTypeNode(orderByInputName), defaultValue: null, new List<DirectiveNode>()),
            };
        }

        public static ObjectTypeDefinitionNode AddQueryArgumentsForRelationships(ObjectTypeDefinitionNode node, Dictionary<string, InputObjectTypeDefinitionNode> inputObjects)
        {
            IEnumerable<FieldDefinitionNode> relationshipFields =
                node.Fields.Where(field => field.Directives.Any(d => d.Name.Value == RelationshipDirectiveType.DirectiveName));

            foreach (FieldDefinitionNode field in relationshipFields)
            {
                if (RelationshipDirectiveType.Cardinality(field) != Cardinality.Many)
                {
                    continue;
                }

                string target = RelationshipDirectiveType.Target(field);

                string targetFilterInputName = InputTypeBuilder.GenerateObjectInputFilterName(target);
                string targetOrderByInputName = InputTypeBuilder.GenerateObjectInputOrderByName(target);

                List<InputValueDefinitionNode> args = QueryArgumentsForField(targetFilterInputName, targetOrderByInputName);

                List<FieldDefinitionNode> fields = node.Fields.ToList();
                fields[fields.FindIndex(f => f.Name == field.Name)] = field.WithArguments(args);

                node = node.WithFields(fields);
            }

            return node;
        }

        public static ObjectType PaginationTypeToModelType(ObjectType underlyingFieldType, IReadOnlyCollection<INamedType> types)
        {
            IEnumerable<ObjectType> modelTypes = types.Where(t => t is ObjectType)
                .Cast<ObjectType>()
                .Where(IsModelType);

            return modelTypes.First(t => t.Name == underlyingFieldType.Name.Replace(PAGINATION_OBJECT_TYPE_SUFFIX, ""));
        }

        public static bool IsPaginationType(ObjectType objectType)
        {
            return objectType.Name.EndsWith(PAGINATION_OBJECT_TYPE_SUFFIX);
        }

        public static bool IsPaginationType(NamedTypeNode objectType)
        {
            return objectType.Name.Value.EndsWith(PAGINATION_OBJECT_TYPE_SUFFIX);
        }

        public static ObjectTypeDefinitionNode GenerateReturnType(NameNode name, bool isAggregationEnabled = false)
        {
            string scalarFieldsEnumName = EnumTypeBuilder.GenerateScalarFieldsEnumName(name.Value);

            List<FieldDefinitionNode> fields = new() {
                    new(
                        location: null,
                        new NameNode(PAGINATION_FIELD_NAME),
                        new StringValueNode("The list of items that matched the filter"),
                        new List<InputValueDefinitionNode>(),
                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(name)))),
                        new List<DirectiveNode>()),
                    new(
                        location: null,
                        new NameNode(PAGINATION_TOKEN_FIELD_NAME),
                        new StringValueNode("A pagination token to provide to subsequent pages of a query"),
                        new List<InputValueDefinitionNode>(),
                        new StringType().ToTypeNode(),
                        new List<DirectiveNode>()),
                    new(
                        location: null,
                        new NameNode(HAS_NEXT_PAGE_FIELD_NAME),
                        new StringValueNode("Indicates if there are more pages of items to return"),
                        new List<InputValueDefinitionNode>(),
                        new NonNullType(new BooleanType()).ToTypeNode(),
                        new List<DirectiveNode>())
                };

            if (isAggregationEnabled)
            {
                fields.Add(
                    new(
                        location: null,
                        new NameNode(GROUP_BY_FIELD_NAME),
                        new StringValueNode("Group results by specified fields"),
                        new List<InputValueDefinitionNode>
                        {
                            new(
                                location: null,
                                new NameNode(GROUP_BY_FIELDS_FIELD_NAME),
                                new StringValueNode("Fields to group by"),
                                new ListTypeNode(new NonNullTypeNode(new NamedTypeNode(scalarFieldsEnumName))),
                                defaultValue: null,
                                new List<DirectiveNode>()
                            )
                        },
                        new NonNullTypeNode(new ListTypeNode(new NonNullTypeNode(new NamedTypeNode($"{name.Value}GroupBy")))),
                        new List<DirectiveNode>())
                );
            }

            return new(
                location: null,
                new NameNode(GeneratePaginationTypeName(name.Value)),
                new StringValueNode("The return object from a filter query that supports a pagination token for paging through results"),
                new List<DirectiveNode>(),
                new List<NamedTypeNode>(),
                fields);
        }

        public static string GeneratePaginationTypeName(string name)
        {
            return $"{name}{PAGINATION_OBJECT_TYPE_SUFFIX}";
        }
    }
}
