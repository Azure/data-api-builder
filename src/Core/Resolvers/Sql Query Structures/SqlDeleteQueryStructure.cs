// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL DELETE query
    ///</summary>
    public class SqlDeleteStructure : BaseSqlQueryStructure
    {
        public SqlDeleteStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            HttpContext httpContext)
        : base(
              metadataProvider: sqlMetadataProvider,
              authorizationResolver: authorizationResolver,
              gQLFilterParser: gQLFilterParser,
              entityName: entityName,
              httpContext: httpContext,
              HttpMethod: EntityActionOperation.Delete)
        {
            SourceDefinition sourceDefinition = GetUnderlyingSourceDefinition();

            List<string> primaryKeys = sourceDefinition.PrimaryKey;
            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                if (param.Value is null)
                {
                    // Should never happen since delete mutations expect non nullable pk params
                    throw new DataApiBuilderException(
                        message: $"Unexpected {param.Key} null argument.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                // primary keys used as predicates
                MetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                if (primaryKeys.Contains(backingColumn!))
                {
                    Predicates.Add(new Predicate(
                        new PredicateOperand(new Column(DatabaseObject.SchemaName, DatabaseObject.Name, backingColumn!)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"{MakeDbConnectionParam(GetParamAsSystemType(param.Value.ToString()!, backingColumn!, GetColumnSystemType(backingColumn!)), backingColumn)}")
                    ));
                }
            }
        }
    }
}
