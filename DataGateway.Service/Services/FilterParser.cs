using System;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// FilterParser stores the model that represents customer data and can
    /// Parse the FilterClause generated from that model.
    /// </summary>
    public class FilterParser
    {
        private IEdmModel? _model;

        public FilterParser() { }

        public FilterParser(DatabaseSchema schema)
        {
            EdmModelBuilder builder = new();
            _model = builder.BuildModel(schema).GetModel();
        }

        /// <summary>
        /// Parses a given filter part of a query string.
        /// </summary>
        /// <param name="filterQueryString">Represents the $filter part of the query string</param>
        /// <param name="resourcePath">Represents the resource path, in our case the entity name.</param>
        /// <returns>An AST FilterClause that represents the filter portion of the WHERE clause.</returns>
        public FilterClause GetFilterClause(string filterQueryString, string resourcePath)
        {
            if (_model == null)
            {
                throw new DataGatewayException(
                    message: "The runtime has not been initialized with an Edm model.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataGatewayException.SubStatusCodes.UnexpectedError);
            }

            try
            {
                Uri relativeUri = new(resourcePath + '/' + filterQueryString, UriKind.Relative);
                ODataUriParser parser = new(_model!, relativeUri);
                return parser.ParseFilter();
            }
            catch (ODataException e)
            {
                throw new DataGatewayException(e.Message, HttpStatusCode.BadRequest, DataGatewayException.SubStatusCodes.BadRequest);
            }
        }
    }
}
