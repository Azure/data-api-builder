using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Azure.DataGateway.Service.Resolvers
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
        public List<OrderByColumn> OrderByColumns { get; internal set; }

        public CosmosQueryStructure(
            IMiddlewareContext context,
            IDictionary<string, object> parameters,
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
        private void Init(IDictionary<string, object> queryParams)
        {
            IFieldSelection selection = _context.Selection;
            ObjectType underlyingType = UnderlyingGraphQLEntityType(selection.Field.Type);

            IsPaginated = QueryBuilder.IsPaginationType(underlyingType);
            OrderByColumns = new();

            if (IsPaginated)
            {
                FieldNode? fieldNode = ExtractItemsQueryField(selection.SyntaxNode);

                if (fieldNode != null)
                {
                    Columns.AddRange(fieldNode.SelectionSet!.Selections.Select(x => new LabelledColumn(tableSchema: string.Empty,
                                                                                                       tableName: _containerAlias,
                                                                                                       columnName: string.Empty,
                                                                                                       label: x.GetNodes().First().ToString())));
                }

                ObjectType realType = UnderlyingGraphQLEntityType(underlyingType.Fields[QueryBuilder.PAGINATION_FIELD_NAME].Type);
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
            if (queryParams.ContainsKey(QueryBuilder.PAGE_START_ARGUMENT_NAME))
            {
                MaxItemCount = (int)queryParams[QueryBuilder.PAGE_START_ARGUMENT_NAME];
                queryParams.Remove(QueryBuilder.PAGE_START_ARGUMENT_NAME);
            }

            if (queryParams.ContainsKey(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME))
            {
                Continuation = (string)queryParams[QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME];
                queryParams.Remove(QueryBuilder.PAGINATION_TOKEN_ARGUMENT_NAME);
            }

            if (queryParams.ContainsKey("orderBy"))
            {
                object? orderByObject = queryParams["orderBy"];

                if (orderByObject != null)
                {
                    OrderByColumns = ProcessGraphQLOrderByArg((List<ObjectFieldNode>)orderByObject);
                }

                queryParams.Remove("orderBy");
            }

            if (queryParams.ContainsKey(QueryBuilder.FILTER_FIELD_NAME))
            {
                object? filterObject = queryParams[QueryBuilder.FILTER_FIELD_NAME];

                if (filterObject != null)
                {
                    List<ObjectFieldNode> filterFields = (List<ObjectFieldNode>)filterObject;
                    Predicates.Add(GQLFilterParser.Parse(fields: filterFields,
                        schemaName: string.Empty,
                        tableName: _containerAlias,
                        tableAlias: _containerAlias,
                        table: new TableDefinition(),
                        processLiterals: MakeParamWithValue));
                }
            }
            else
            {
                foreach (KeyValuePair<string, object> parameter in queryParams)
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(tableSchema: string.Empty, _containerAlias, parameter.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{MakeParamWithValue(parameter.Value)}")
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

                if (enumValue.Value == $"{OrderByDir.Desc}")
                {
                    orderByColumnsList.Add(new OrderByColumn(tableSchema: string.Empty, _containerAlias, fieldName, direction: OrderByDir.Desc));
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
