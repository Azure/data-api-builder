using System;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Parsers
{
    /// <summary>
    /// ODataParser stores the model that represents customer data and can
    /// parse the filter query string, order by query string, or database policy from the configuration file permissions section.
    /// </summary>
    public class ODataParser
    {
        private IEdmModel? _model;

        public ODataParser() { }

        public void BuildModel(ISqlMetadataProvider sqlMetadataProvider)
        {
            EdmModelBuilder builder = new();
            _model = builder.BuildModel(sqlMetadataProvider).GetModel();
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

        public OrderByClause GetOrderByClause(string sortQueryString, string path)
        {
            try
            {
                Uri relativeUri = new(path + '/' + sortQueryString, UriKind.Relative);
                ODataUriParser parser = new(_model, relativeUri);
                return parser.ParseOrderBy();
            }
            catch (ODataException e)
            {
                throw new DataGatewayException(e.Message, HttpStatusCode.BadRequest, DataGatewayException.SubStatusCodes.BadRequest);
            }
        }
    }
}
