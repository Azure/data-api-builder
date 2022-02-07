using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;

namespace Azure.DataGateway.Service.Resolvers
{
    public class CosmosQueryStructure : BaseSqlQueryStructure
    {
        private IMiddlewareContext _context;
        public bool IsPaginated { get; }

        private readonly string _containerAlias = "c";
        public string Continuation { get; internal set; }
        public long MaxItemCount { get; internal set; }

        public CosmosQueryStructure(IMiddlewareContext context,
            IDictionary<string, object> parameters,
            IMetadataStoreProvider metadataStoreProvider,
            bool isPaginatedQuery) : base(metadataStoreProvider)
        {
            _context = context;
            IsPaginated = isPaginatedQuery;
            Init(parameters);
        }

        private void Init(IDictionary<string, object> queryParams)
        {
            if (IsPaginated)
            {
                SelectionSetNode selectionSet = ((FieldNode)_context.Selection.SyntaxNode.SelectionSet.Selections[0]).SelectionSet;
                Columns.AddRange(selectionSet.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
            }
            else
            {
                Columns.AddRange(_context.Selection
                    .SyntaxNode.SelectionSet.Selections.Select(x => new LabelledColumn(_containerAlias, "", x.ToString())));
            }

            foreach (KeyValuePair<string, object> parameter in queryParams)
            {
                // first and after will not be part of query parameters. They will be going into headers instead.
                // TODO: Revisit 'first' while adding support for TOP queries
                if (parameter.Key == "first")
                {
                    MaxItemCount = (long)parameter.Value;
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
