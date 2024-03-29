// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;

namespace Azure.DataApiBuilder.Core.Resolvers
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
            GQLMutArgumentToDictParams(context, MutationBuilder.ITEM_INPUT_ARGUMENT_NAME, mutationParams),
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
              operationType: EntityActionOperation.Create)
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
                // If the size of this set FieldsReferencedInDbPolicyForCreateAction is 0,
                // it implies that all the fields referenced in the database policy for create action are being included in the insert statement, and we are good.
                // However, if the size is non-zero, we throw a Forbidden request exception.
                throw new DataApiBuilderException(
                    message: "One or more fields referenced by the database policy are not present in the request body.",
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed);
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

            // As we add columns to the InsertColumns list for SqlInsertQueryStructure one by one,
            // we remove the columns (if present) from the FieldsReferencedInDbPolicyForCreateAction.
            // This is necessary because any field referenced in database policy but not present in insert statement would result in an exception.
            FieldsReferencedInDbPolicyForCreateAction.Remove(columnName);

            string paramName;

            if (value is not null)
            {
                paramName = MakeDbConnectionParam(
                    GetParamAsSystemType(value.ToString()!, columnName, GetColumnSystemType(columnName)), columnName);
            }
            else
            {
                paramName = MakeDbConnectionParam(null, columnName);
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
