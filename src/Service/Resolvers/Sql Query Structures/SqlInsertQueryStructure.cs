using System;
using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Models;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Resolvers;
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
            IDictionary<string, object?> mutationParams
        ) : this(
            entityName,
            sqlMetadataProvider,
            GQLMutArgumentToDictParams(context, CreateMutationBuilder.INPUT_ARGUMENT_NAME, mutationParams))
        { }

        public SqlInsertStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams
            )
        : base(sqlMetadataProvider, entityName: entityName)
        {
            InsertColumns = new();
            Values = new();
            OutputColumns = GenerateOutputColumns();

            foreach (KeyValuePair<string, object?> param in mutationParams)
            {
                SqlMetadataProvider.TryGetBackingColumn(EntityName, param.Key, out string? backingColumn);
                PopulateColumnsAndParams(backingColumn!, param.Value);
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
            string paramName;

            try
            {
                if (value != null)
                {
                    paramName = MakeParamWithValue(
                        GetParamAsColumnSystemType(value.ToString()!, columnName));
                }
                else
                {
                    paramName = MakeParamWithValue(null);
                }

                Values.Add($"@{paramName}");
            }
            catch (ArgumentException ex)
            {
                throw new DataApiBuilderException(
                    message: ex.Message,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest,
                    innerException: ex);
            }
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
