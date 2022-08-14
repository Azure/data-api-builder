using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.GraphQLBuilder;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class CosmosQueryStructure : BaseQueryStructure
    {
        private readonly IMiddlewareContext _context;
        private readonly ISqlMetadataProvider _metadataProvider;

        public bool IsPaginated { get; internal set; }

        private readonly string _containerAlias = "c";
        public string Container { get; internal set; }
        public string Database { get; internal set; }
        public string? Continuation { get; internal set; }
        public int MaxItemCount { get; internal set; }
        public string? PartitionKeyValue { get; internal set; }
        public List<OrderByColumn> OrderByColumns { get; internal set; }

        public CosmosQueryStructure(
            IMiddlewareContext context,
            IDictionary<string, object?> parameters,
            ISqlMetadataProvider metadataProvider)
            : base()
        {
            _metadataProvider = metadataProvider;
            _context = context;
            Init(parameters);
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
                    Columns.AddRange(fieldNode.SelectionSet!.Selections.Select(x => new LabelledColumn(tableSchema: string.Empty,
                                                                                                       tableName: _containerAlias,
                                                                                                       columnName: string.Empty,
                                                                                                       label: x.GetNodes().First().ToString())));
                }

                ObjectType realType = GraphQLUtils.UnderlyingGraphQLEntityType(underlyingType.Fields[QueryBuilder.PAGINATION_FIELD_NAME].Type);
                string entityName = realType.Name;

                Database = _metadataProvider.GetSchemaName(entityName);
                Container = _metadataProvider.GetDatabaseObjectName(entityName);
            }
            else
            {
                Columns.AddRange(selection.SyntaxNode.SelectionSet!.Selections.Select(x => new LabelledColumn(tableSchema: string.Empty,
                                                                                                              tableName: _containerAlias,
                                                                                                              columnName: string.Empty,
                                                                                                              label: x.GetNodes().First().ToString())));

                string entityName = underlyingType.Name;

                Database = _metadataProvider.GetSchemaName(entityName);
                Container = _metadataProvider.GetDatabaseObjectName(entityName);
            }

            // first and after will not be part of query parameters. They will be going into headers instead.
            // TODO: Revisit 'first' while adding support for TOP queries
            if (queryParams.TryGetValue(QueryBuilder.PAGE_START_ARGUMENT_NAME, out object? maxItemCount) && maxItemCount is not null)
            {
                MaxItemCount = (int)maxItemCount;
                queryParams.Remove(QueryBuilder.PAGE_START_ARGUMENT_NAME);
            }

            if (queryParams.TryGetValue(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME, out object? continuation) && continuation is not null)
            {
                Continuation = (string)continuation;
                queryParams.Remove(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME);
            }

            if (queryParams.TryGetValue(QueryBuilder.PARTITION_KEY_FIELD_NAME, out object? pk) && pk is not null)
            {
                PartitionKeyValue = (string)pk;
                queryParams.Remove(QueryBuilder.PARTITION_KEY_FIELD_NAME);
            }

            if (queryParams.TryGetValue(QueryBuilder.ORDER_BY_FIELD_NAME, out object? orderBy) && orderBy is not null)
            {
                OrderByColumns = ProcessGraphQLOrderByArg((IDictionary<string, object?>)orderBy);
                queryParams.Remove(QueryBuilder.ORDER_BY_FIELD_NAME);
            }

            if (queryParams.TryGetValue(QueryBuilder.FILTER_FIELD_NAME, out object? filterObject))
            {
                if (filterObject is not null)
                {
                    IDictionary<string, object?> filterFields = (IDictionary<string, object?>)filterObject;
                    Predicates.Add(GraphQLFilterParsers.Parse(
                        fields: filterFields,
                        schemaName: string.Empty,
                        tableName: _containerAlias,
                        tableAlias: _containerAlias,
                        table: new TableDefinition(),
                        processLiterals: MakeParamWithValue));
                }
            }
            else
            {
                foreach ((string key, object? value) in queryParams)
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(tableSchema: string.Empty, _containerAlias, key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(value)}")
                    ));
                }
            }
        }

        /// <summary>
        /// Create a list of orderBy columns from the orderBy argument
        /// passed to the gql query
        /// </summary>
        private List<OrderByColumn> ProcessGraphQLOrderByArg(IDictionary<string, object?> orderByFields)
        {
            // Create list of primary key columns
            // we always have the primary keys in
            // the order by statement for the case
            // of tie breaking and pagination
            List<OrderByColumn> orderByColumnsList = new();

            foreach ((string fieldName, object? value) in orderByFields)
            {
                if (value is null)
                {
                    continue;
                }

                OrderBy direction = Enum.Parse<OrderBy>((string)value);
                orderByColumnsList.Add(new OrderByColumn(tableSchema: string.Empty, _containerAlias, fieldName, direction: direction));
            }

            return orderByColumnsList;
        }
    }
}
