using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            Assert.IsNotNull(schema);
            EdmModelBuilder builder = new();
            _model = builder.BuildModel(schema).GetModel();
        }

        /// <summary>
        /// Parses the filter clause.
        /// </summary>
        /// <returns>A list of rest predicates to be used in query generation.</returns>
        public List<RestPredicate> Parse(string filterQueryString, string resourcePath)
        {
            // this is not proper Uri format
            // find out what we need for relative uri format and match
            //Uri relativeUri = new(queryString);
            //
            Uri serviceRoot = new("https://localhost:5001/");
            Uri fullUri = new(serviceRoot + resourcePath + filterQueryString);
            ODataUriParser parser = new(_model, serviceRoot, fullUri);
            FilterClause filterClause = parser.ParseFilter();
            ODataASTVisitor<object> visitor = new();
            filterClause.Expression.Accept(visitor);
            return visitor.TryAndGetRestPredicates();
        }
    }
}
