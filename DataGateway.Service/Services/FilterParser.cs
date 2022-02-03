using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.Edm;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// FilterParser stores the model that represents customer data and can
    /// Parse the FilterClause generated from that model.
    /// </summary>
    public class FilterParser
    {
        private IEdmModel _model;

        public FilterParser(DatabaseSchema schema)
        {
            if (schema is null)
            {
                return;
            }

            EdmModelBuilder builder = new();
            _model = builder.BuildModel(schema).GetModel();
        }

        /// <summary>
        /// Parses the filter clause.
        /// </summary>
        /// <returns>A list of rest predicates to be used in query generation.</returns>
        public Dictionary<string, Tuple<object, PredicateOperation>> Parse()
        {
            throw new NotImplementedException();
        }
    }
}
