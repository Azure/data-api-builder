// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using HotChocolate.Language;
using Microsoft.OData;
using Microsoft.OData.Edm;
using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
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

        public void BuildModel(DocumentNode graphQLSchemaRoot)
        {
            EdmModelBuilder builder = new();
            _model = builder.BuildModel(graphQLSchemaRoot).GetModel();
        }

        /// <summary>
        /// Parses a given filter part of a query string.
        /// </summary>
        /// <param name="filterQueryString">Represents the $filter part of the query string</param>
        /// <param name="resourcePath">Represents the resource path, in our case the entity name.</param>
        /// <param name="customResolver">ODataUriResolver resolving different kinds of Uri parsing context.</param>
        /// <param name="parameterAliasNodes">Typed AST values for parameter aliases referenced by the filter.</param>
        /// <returns>An AST FilterClause that represents the filter portion of the WHERE clause.</returns>
        public FilterClause GetFilterClause(
            string filterQueryString,
            string resourcePath,
            ODataUriResolver? customResolver = null,
            IReadOnlyDictionary<string, SingleValueNode>? parameterAliasNodes = null)
        {
            if (_model is null)
            {
                throw new DataApiBuilderException(
                    message: "The runtime has not been initialized with an Edm model.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            try
            {
                Uri relativeUri = new(resourcePath + '/' + filterQueryString, UriKind.Relative);
                ODataUriParser parser = new(_model, relativeUri);

                if (customResolver is not null)
                {
                    parser.Resolver = customResolver;
                }

                if (parameterAliasNodes is { Count: > 0 })
                {
                    foreach ((string alias, SingleValueNode valueNode) in parameterAliasNodes)
                    {
                        parser.ParameterAliasNodes.Add(alias, valueNode);
                    }
                }

                FilterClause filterClause = parser.ParseFilter();
                return parameterAliasNodes is not { Count: > 0 }
                    ? filterClause
                    : new ParameterAliasRewriter(parameterAliasNodes).Rewrite(filterClause);
            }
            catch (ODataException e)
            {
                throw new DataApiBuilderException(
                    e.Message,
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: e);
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
                throw new DataApiBuilderException(
                    e.Message,
                    HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: e);
            }
        }
    }
}
