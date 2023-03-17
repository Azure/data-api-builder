// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Represents the query used for an EXISTS clause.
    /// e.g.
    /// EXISTS (
    /// SELECT 1
    /// FROM <sourcename> AS <sourcealias>
    /// WHERE <sourcealias>.[column] = <value>
    /// )
    /// </summary>
    public class SqlExistsQueryStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Constructor for Exists query.
        /// </summary>
        /// <exception cref="DataApiBuilderException">if middleware context doesn't have an httpcontext</exception>
        public SqlExistsQueryStructure(
            HttpContext httpContext,
            ISqlMetadataProvider metadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            List<Predicate> predicates,
            string entityName,
            IncrementingInteger? counter = null)
            : base(
                  metadataProvider,
                  authorizationResolver,
                  gQLFilterParser,
                  predicates,
                  entityName,
                  counter,
                  httpContext,
                  Config.Operation.Read)
        {
            SourceAlias = CreateTableAlias();
        }
    }
}
