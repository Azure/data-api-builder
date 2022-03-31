using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
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

            if (IsPaginated)
            {
                FieldNode? fieldNode = ExtractItemsQueryField(selection.SyntaxNode);
                graphqlType = MetadataStoreProvider.GetGraphQLType(UnderlyingType(ExtractItemsSchemaField(selection.Field).Type).Name);

                if (fieldNode != null)
                {
                    Columns.AddRange(fieldNode.SelectionSet!.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
                }
            }
            else
            {
                Columns.AddRange(selection.SyntaxNode.SelectionSet!.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
            }

            Container = graphqlType.ContainerName;
            Database = graphqlType.DatabaseName;

            foreach (KeyValuePair<string, object> parameter in queryParams)
            {
                // first and after will not be part of query parameters. They will be going into headers instead.
                // TODO: Revisit 'first' while adding support for TOP queries
                if (parameter.Key == "first")
                {
                    MaxItemCount = (int)parameter.Value;
                    continue;
                }

                if (parameter.Key == "after")
                {
                    Continuation = (string)parameter.Value;
                    continue;
                }

                Predicates.Add(new Predicate(
                    new PredicateOperand(new Column(_containerAlias, parameter.Key)),
                    PredicateOperation.Equal,
                    new PredicateOperand($"@{MakeParamWithValue(parameter.Value)}")
                ));
            }
        }
    }
}
