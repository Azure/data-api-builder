// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosQueryStructure : BaseQueryStructure
    {
        private readonly IMiddlewareContext _context;
        private readonly string _containerAlias = "c";

        public override string SourceAlias { get => base.SourceAlias; set => base.SourceAlias = value; }

        public bool IsPaginated { get; internal set; }

        public string Container { get; internal set; }
        public string Database { get; internal set; }
        public string? Continuation { get; internal set; }
        public int? MaxItemCount { get; internal set; }
        public string? PartitionKeyValue { get; internal set; }
        public List<OrderByColumn> OrderByColumns { get; internal set; }

        public CosmosQueryStructure(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser)
            : base(metadataProvider, authorizationResolver, gQLFilterParser, entityName: string.Empty)
        {
            _context = context;
            SourceAlias = _containerAlias;
            DatabaseObject.Name = _containerAlias;
            Init(parameters);
        }

        /// <inheritdoc/>
        public override string MakeDbConnectionParam(object? value, string? columnName = null)
        {
            string encodedParamName = $"{PARAM_NAME_PREFIX}param{Counter.Next()}";
            Parameters.Add(encodedParamName, new(value));
            return encodedParamName;
        }

        private static IEnumerable<LabelledColumn> GenerateQueryColumns(SelectionSetNode selectionSet, DocumentNode document, string tableName)
        {
            foreach (ISelectionNode selectionNode in selectionSet.Selections)
            {
                if (selectionNode.Kind == SyntaxKind.FragmentSpread)
                {
                    FragmentSpreadNode fragmentSpread = (FragmentSpreadNode)selectionNode;
                    FragmentDefinitionNode fragmentDocumentNode = document.GetNodes()
                        .Where(n => n.Kind == SyntaxKind.FragmentDefinition)
                        .Cast<FragmentDefinitionNode>()
                        .Where(n => n.Name.Value == fragmentSpread.Name.Value)
                        .First();

                    foreach (LabelledColumn column in GenerateQueryColumns(fragmentDocumentNode.SelectionSet, document, tableName))
                    {
                        yield return column;
                    }
                }
                else if (selectionNode.Kind == SyntaxKind.InlineFragment)
                {
                    InlineFragmentNode inlineFragment = (InlineFragmentNode)selectionNode;
                    foreach (LabelledColumn column in GenerateQueryColumns(inlineFragment.SelectionSet, document, tableName))
                    {
                        yield return column;
                    }
                }
                else
                {
                    yield return new LabelledColumn(
                        tableSchema: string.Empty,
                        tableName: tableName,
                        columnName: string.Empty,
                        label: selectionNode.GetNodes().First().ToString());
                }
            }
        }

        [MemberNotNull(nameof(Container))]
        [MemberNotNull(nameof(Database))]
        [MemberNotNull(nameof(OrderByColumns))]
        private void Init(IDictionary<string, object?> queryParams)
        {
            IFieldSelection selection = _context.Selection;
            ObjectType underlyingType = GraphQLUtils.UnderlyingGraphQLEntityType(selection.Field.Type);

            IsPaginated = QueryBuilder.IsPaginationType(underlyingType);
            OrderByColumns = new();

            if (IsPaginated)
            {
                FieldNode? fieldNode = ExtractItemsQueryField(selection.SyntaxNode);

                if (fieldNode is not null)
                {
                    Columns.AddRange(GenerateQueryColumns(fieldNode.SelectionSet!, _context.Document, SourceAlias));
                }

                ObjectType realType = GraphQLUtils.UnderlyingGraphQLEntityType(underlyingType.Fields[QueryBuilder.PAGINATION_FIELD_NAME].Type);
                string entityName = MetadataProvider.GetEntityName(realType.Name);
                EntityName = entityName;
                Database = MetadataProvider.GetSchemaName(entityName);
                Container = MetadataProvider.GetDatabaseObjectName(entityName);
            }
            else
            {
                Columns.AddRange(GenerateQueryColumns(selection.SyntaxNode.SelectionSet!, _context.Document, SourceAlias));
                string typeName = GraphQLUtils.TryExtractGraphQLFieldModelName(underlyingType.Directives, out string? modelName) ?
                    modelName :
                    underlyingType.Name;
                string entityName = MetadataProvider.GetEntityName(typeName);
                EntityName = entityName;
                Database = MetadataProvider.GetSchemaName(entityName);
                Container = MetadataProvider.GetDatabaseObjectName(entityName);
            }

            // first and after will not be part of query parameters. They will be going into headers instead.
            // TODO: Revisit 'first' while adding support for TOP queries
            if (queryParams.ContainsKey(QueryBuilder.PAGE_START_ARGUMENT_NAME))
            {
                MaxItemCount = (int?)queryParams[QueryBuilder.PAGE_START_ARGUMENT_NAME];
                queryParams.Remove(QueryBuilder.PAGE_START_ARGUMENT_NAME);
            }

            if (queryParams.ContainsKey(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME))
            {
                Continuation = (string?)queryParams[QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME];
                queryParams.Remove(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME);
            }

            if (queryParams.ContainsKey(QueryBuilder.PARTITION_KEY_FIELD_NAME))
            {
                PartitionKeyValue = (string?)queryParams[QueryBuilder.PARTITION_KEY_FIELD_NAME];
                queryParams.Remove(QueryBuilder.PARTITION_KEY_FIELD_NAME);
            }

            if (queryParams.ContainsKey("orderBy"))
            {
                object? orderByObject = queryParams["orderBy"];

                if (orderByObject is not null)
                {
                    OrderByColumns = ProcessGraphQLOrderByArg((List<ObjectFieldNode>)orderByObject);
                }

                queryParams.Remove("orderBy");
            }

            if (queryParams.ContainsKey(QueryBuilder.FILTER_FIELD_NAME))
            {
                object? filterObject = queryParams[QueryBuilder.FILTER_FIELD_NAME];

                if (filterObject is not null)
                {
                    List<ObjectFieldNode> filterFields = (List<ObjectFieldNode>)filterObject;
                    Predicates.Add(
                        GraphQLFilterParser.Parse(
                            _context,
                            filterArgumentSchema: selection.Field.Arguments[QueryBuilder.FILTER_FIELD_NAME],
                            fields: filterFields,
                            queryStructure: this));

                    // after parsing all the GraphQL filters,
                    // reset the source alias and object name to the generic container alias
                    // since these may potentially be updated due to the presence of nested filters.
                    SourceAlias = _containerAlias;
                    DatabaseObject.Name = _containerAlias;
                }
            }
            else
            {
                foreach (KeyValuePair<string, object?> parameter in queryParams)
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(tableSchema: string.Empty, _containerAlias, parameter.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"{MakeDbConnectionParam(parameter.Value)}")
                    ));
                }
            }
        }

        /// <summary>
        /// Create a list of orderBy columns from the orderBy argument
        /// passed to the gql query
        /// </summary>
        private List<OrderByColumn> ProcessGraphQLOrderByArg(List<ObjectFieldNode> orderByFields)
        {
            // Create list of primary key columns
            // we always have the primary keys in
            // the order by statement for the case
            // of tie breaking and pagination
            List<OrderByColumn> orderByColumnsList = new();

            foreach (ObjectFieldNode field in orderByFields)
            {
                if (field.Value is NullValueNode)
                {
                    continue;
                }

                string fieldName = field.Name.ToString();

                EnumValueNode enumValue = (EnumValueNode)field.Value;

                if (enumValue.Value == $"{OrderBy.DESC}")
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: string.Empty, _containerAlias, fieldName, direction: OrderBy.DESC));
                }
                else
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: string.Empty, _containerAlias, fieldName));
                }
            }

            return orderByColumnsList;
        }
    }
}
