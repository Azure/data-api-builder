using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosQueryStructure : BaseQueryStructure
    {
        private IMiddlewareContext _context;
        public bool IsPaginated { get; internal set; }

        private readonly string _containerAlias = "c";
        public string Container { get; internal set; }
        public string Database { get; internal set; }
        public string? Continuation { get; internal set; }
        public int MaxItemCount { get; internal set; }
        public List<OrderByColumn> OrderByColumns { get; internal set; }

        protected IGraphQLMetadataProvider MetadataStoreProvider { get; }

        public CosmosQueryStructure(IMiddlewareContext context,
            IDictionary<string, object> parameters,
            IGraphQLMetadataProvider metadataStoreProvider)
            : base()
        {
            MetadataStoreProvider = metadataStoreProvider;
            _context = context;
            Init(parameters);
        }

        [MemberNotNull(nameof(Container))]
        [MemberNotNull(nameof(Database))]
        private void Init(IDictionary<string, object> queryParams)
        {
            IFieldSelection selection = _context.Selection;
            GraphQLType graphqlType = MetadataStoreProvider.GetGraphQLType(UnderlyingType(selection.Field.Type).Name);
            IsPaginated = graphqlType.IsPaginationType;
            OrderByColumns = new();

            if (IsPaginated)
            {
                FieldNode? fieldNode = ExtractItemsQueryField(selection.SyntaxNode);
                graphqlType = MetadataStoreProvider.GetGraphQLType(UnderlyingType(ExtractItemsSchemaField(selection.Field).Type).Name);

                if (fieldNode != null)
                {
                    Columns.AddRange(fieldNode.SelectionSet!.Selections.Select(x => new LabelledColumn(tableSchema: string.Empty,
                                                                                                       tableName: _containerAlias,
                                                                                                       columnName: string.Empty,
                                                                                                       label: x.GetNodes().First().ToString())));
                }
            }
            else
            {
                Columns.AddRange(selection.SyntaxNode.SelectionSet!.Selections.Select(x => new LabelledColumn(tableSchema: string.Empty,
                                                                                                              tableName: _containerAlias,
                                                                                                              columnName: string.Empty,
                                                                                                              label: x.GetNodes().First().ToString())));
            }

            Container = graphqlType.ContainerName;
            Database = graphqlType.DatabaseName;

            // first and after will not be part of query parameters. They will be going into headers instead.
            // TODO: Revisit 'first' while adding support for TOP queries
            if (queryParams.ContainsKey("first"))
            {
                MaxItemCount = (int)queryParams["first"];
                queryParams.Remove("first");
            }

            if (queryParams.ContainsKey("after"))
            {
                Continuation = (string)queryParams["after"];
                queryParams.Remove("after");
            }

            if (queryParams.ContainsKey("orderBy"))
            {
                object? orderByObject = queryParams["orderBy"];

                if (orderByObject != null)
                {
                    OrderByColumns = ProcessGqlOrderByArg((List<ObjectFieldNode>)orderByObject);
                }
            }

            if (queryParams.ContainsKey("_filter"))
            {
                object? filterObject = queryParams["_filter"];

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
        private List<OrderByColumn> ProcessGqlOrderByArg(List<ObjectFieldNode> orderByFields)
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
