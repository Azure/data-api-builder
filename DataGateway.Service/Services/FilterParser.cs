using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
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
        private IEdmModel _model;

        public FilterParser(DatabaseSchema schema)
        {
            _schema = schema;
            EdmModelBuilder builder = new();
            _model = builder.BuildModel(_schema).GetModel();
        }

        public List<RestPredicate> Parse()
        {
            throw new NotImplementedException();
        }
    }
}
