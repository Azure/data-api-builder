// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Wraps all the required data and logic to write a SQL INSERT query
    /// </summary>
    public class SqlInsertStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Column names to insert into the given columns
        /// </summary>
        public List<string> InsertColumns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// The inserted columns that the insert will OUTPUT
        /// </summary>
        public List<LabelledColumn> OutputColumns { get; }

        public SqlInsertStructure(
            IMiddlewareContext context,
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            HttpContext httpContext
        ) : this(
            entityName,
            sqlMetadataProvider,
            authorizationResolver,
            gQLFilterParser,
            GQLMutArgumentToDictParams(context, CreateMutationBuilder.INPUT_ARGUMENT_NAME, mutationParams),
            httpContext)
        { }

        public SqlInsertStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IAuthorizationResolver authorizationResolver,
            GQLFilterParser gQLFilterParser,
            IDictionary<string, object?> mutationParams,
            HttpContext httpContext
            )
        : base(
              metadataProvider: sqlMetadataProvider,
              authorizationResolver: authorizationResolver,
              gQLFilterParser: gQLFilterParser,
              entityName: entityName,
              httpContext: httpContext,
              operationType: Config.Operation.Create)
        {
            InsertColumns = new();
            Values = new();
            OutputColumns = GenerateOutputColumns();

            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                MetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                PopulateColumnsAndParams(backingColumn!, param.Value);
            }

            if (FieldsReferencedInDbPolicyForCreateAction.Count > 0)
            {
                // This indicates that one or more fields referenced in the database policy are not a part of the insert statement.
                throw new DataApiBuilderException(
                    message: "One or more fields referenced by the database policy are not present in the request body.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Populates the column name in Columns, creates parameter
        /// and adds its value to Values.
        /// </summary>
        /// <param name="columnName">The name of the column.</param>
        /// <param name="value">The value of the column.</param>
        private void PopulateColumnsAndParams(string columnName, object? value)
        {
            InsertColumns.Add(columnName);

            // If the column is referenced in the database policy, we remove it from the set.
            FieldsReferencedInDbPolicyForCreateAction.Remove(columnName);

            string paramName;

            if (value is not null)
            {
                paramName = MakeParamWithValue(
                    GetParamAsSystemType(value.ToString()!, columnName, GetColumnSystemType(columnName)));
            }
            else
            {
                paramName = MakeParamWithValue(value: null);
            }

            Values.Add($"{paramName}");
        }

        /// <summary>
        /// Get the definition of a column by name
        /// </summary>
        public ColumnDefinition GetColumnDefinition(string columnName)
        {
            return GetUnderlyingSourceDefinition().Columns[columnName];
        }
    }
}
