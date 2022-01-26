using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.OData.Edm;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// FilterParser stores the model that represents customer data and returns
    /// a list of RestPredicates representing the predicates required for the Query
    /// that matches the provided $filter query string.
    /// </summary>
    public class FilterParser
    {
        private DatabaseSchema _schema;
        private EdmModelBuilder _builder;
        private IEdmModel _model;

        public FilterParser(IMetadataStoreProvider metadataStoreProvider)
        {
            _schema = metadataStoreProvider.GetResolvedConfig().DatabaseSchema;
            _builder = new();

            // should we use dependancy injection here to avoid recreating the model?
            _model = _builder
                .BuildEntityTypes(_schema, nullable: false)
                .BuildEntitySets(_schema)
                .GetModel();
        }

        public List<RestPredicate> Parse()
        {
            throw new NotImplementedException();
        }
    }
}
