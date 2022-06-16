using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.GraphQLBuilder.Mutations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Wraps all the required data and logic to write a SQL INSERT query
    /// </summary>
    public class SqlInsertStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Column names to insert into the given columns
        /// </summary>
        public List<MutationColumn> InsertColumns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// The inserted columns that the insert will return
        /// </summary>
        public List<string> ReturnColumns { get; }

        public SqlInsertStructure(
            string entityName,
            ISqlMetadataProvider sqlMetadataProvider,
            IDictionary<string, object?> mutationParams)
        : base(sqlMetadataProvider, entityName: entityName)
        {
            InsertColumns = new();
            Values = new();

            TableDefinition tableDefinition = GetUnderlyingTableDefinition();

            ReturnColumns = tableDefinition.Columns.Keys.ToList();

            IDictionary<string, object?> createInput =
                InputArgumentToMutationParams(mutationParams, CreateMutationBuilder.INPUT_ARGUMENT_NAME);

            foreach (KeyValuePair<string, object?> param in createInput)
            {
                PopulateColumnsAndParams(param.Key, param.Value);
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
            SqlMetadataProvider.TryGetExposedColumnName(EntityName, columnName, out string? exposedName);
            InsertColumns.Add(new (columnName, exposedName!));
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
                throw new DataGatewayException(
                    message: ex.Message,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Get the definition of a column by name
        /// </summary>
        public ColumnDefinition GetColumnDefinition(string columnName)
        {
            return GetUnderlyingTableDefinition().Columns[columnName];
        }
    }
}
