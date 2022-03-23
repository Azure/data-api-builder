using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL query resembling an UPSERT operation.
    ///</summary>
    public class SqlUpsertQueryStructure : BaseSqlQueryStructure
    {
        /// <summary>
        /// Names of columns that will be populated with values during the insert operation.
        /// </summary>
        public List<string> InsertColumns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// Updates to be applied to selected row
        /// </summary>
        public List<Predicate> UpdateOperations { get; }

        /// <summary>
        /// The updated columns that the update will return
        /// </summary>
        public List<string> ReturnColumns { get; }

        /// <summary>
        /// Indicates whether PK is autogenerated so an Upsert (insert) fails if value is true.
        /// </summary>
        public bool IsPKAutogenerated { get; set; }

        /// <summary>
        /// Maps a column name to the created parameter name to avoid creating
        /// duplicate parameters. Useful in Upsert where an Insert and Update
        /// structure are both created.
        /// </summary>
        private Dictionary<string, string> ColumnToParam { get; }

        /// <summary>
        /// An upsert query must be prepared to be utilized for either an UPDATE or INSERT.
        ///
        /// </summary>
        /// <param name="tableName"></param>
        /// <param name="metadataStore"></param>
        /// <param name="mutationParams"></param>
        /// <exception cref="DataGatewayException"></exception>
        public SqlUpsertQueryStructure(string tableName, IMetadataStoreProvider metadataStore, IDictionary<string, object> mutationParams, bool incrementalUpdate)
        : base(metadataStore)
        {
            TableName = tableName;
            UpdateOperations = new();
            InsertColumns = new();
            Values = new();
            ColumnToParam = new();

            TableDefinition tableDefinition = GetTableDefinition();
            SetIsPKAutoGenerated(tableDefinition);

            // All columns will be returned whether upsert results in UPDATE or INSERT
            ReturnColumns = tableDefinition.Columns.Keys.ToList();

            // Populates the UpsertQueryStructure with UPDATE and INSERT column:value metadata
            PopulateColumns(mutationParams, tableDefinition, isIncrementalUpdate: incrementalUpdate);

            if (UpdateOperations.Count == 0)
            {
                throw new DataGatewayException(
                    message: "Update mutation does not update any values",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Get the definition of a column by name
        /// </summary>
        public ColumnDefinition GetColumnDefinition(string columnName)
        {
            return GetTableDefinition().Columns[columnName];
        }

        private void PopulateColumns(
            IDictionary<string, object> mutationParams,
            TableDefinition tableDefinition,
            bool isIncrementalUpdate)
        {
            List<string> primaryKeys = tableDefinition.PrimaryKey;
            List<string> schemaColumns = tableDefinition.Columns.Keys.ToList();

            try
            {
                foreach (KeyValuePair<string, object> param in mutationParams)
                {
                    // Create Parameter and map it to column for downstream logic to utilize.
                    string paramIdentifier = MakeParamWithValue(GetParamAsColumnSystemType(param.Value.ToString()!, param.Key));

                    ColumnToParam.Add(param.Key, paramIdentifier);

                    // Create a predicate for UPDATE Operation.
                    Predicate predicate = new(
                        new PredicateOperand(new Column(null, param.Key)),
                        PredicateOperation.Equal,
                        new PredicateOperand($"@{paramIdentifier}")
                    );

                    // We are guaranteed by the RequestValidator, that a primary key column is in the URL, not body.
                    // That means we must add the PK as predicate for the update request,
                    // as Update request uses Where clause to target item by PK.
                    if (primaryKeys.Contains(param.Key))
                    {
                        PopulateColumnsAndParams(param.Key, param.Value);

                        // PK added as predicate for Update Operation
                        Predicates.Add(predicate);

                        // Track which columns we've acted upon,
                        // so we can add nullified remainder columns later.
                        schemaColumns.Remove(param.Key);
                    }
                    // No need to check param.key exists in schema as invalid columns are caught in RequestValidation.
                    else
                    {
                        // Update Operation. Add since mutation param is not a PK.
                        UpdateOperations.Add(predicate);
                        schemaColumns.Remove(param.Key);

                        // Insert Operation, create record with request specified value.
                        PopulateColumnsAndParams(param.Key, param.Value);
                    }
                }

                // Process remaining columns in schemaColumns.
                if (!isIncrementalUpdate)
                {
                    // UpdateOperations will be modified and have nullable values added for update when appropriate
                    AddNullifiedUnspecifiedFields(schemaColumns, UpdateOperations, tableDefinition);
                }
            }
            catch (ArgumentException ex)
            {
                // ArgumentException thrown from GetParamAsColumnSystemType()
                throw new DataGatewayException(
                    message: ex.Message,
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Populates the column name in Columns, gets created parameter
        /// and adds its value to Values.
        /// </summary>
        /// <param name="columnName">The name of the column.</param>
        /// <param name="value">The value of the column.</param>
        private void PopulateColumnsAndParams(string columnName, object value)
        {
            InsertColumns.Add(columnName);
            string paramName;

            if (value != null)
            {
                //Check parameter Dictionary/List
                paramName = ColumnToParam[columnName];
            }
            else
            {
                // This case should not arise. We have issue for this to handle nullable type columns. Issue #146.
                throw new DataGatewayException(
                    message: $"Unexpected value for column \"{columnName}\" provided.",
                    statusCode: HttpStatusCode.BadRequest,
                    subStatusCode: DataGatewayException.SubStatusCodes.BadRequest);
            }

            Values.Add($"@{paramName}");
        }

        /// <summary>
        /// Sets value of class member IsPKAutogenerated to designate when a Upsert(Insert) request should fail.
        /// </summary>
        /// <param name="tableDef"></param>
        private void SetIsPKAutoGenerated(TableDefinition tableDef)
        {
            bool pkIsAutogenerated = false;
            foreach (string primaryKey in tableDef.PrimaryKey)
            {
                if (tableDef.Columns[primaryKey].IsAutoGenerated)
                {
                    pkIsAutogenerated = true;
                    break;
                }
            }

            IsPKAutogenerated = pkIsAutogenerated;
        }
    }
}
