using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Wraps all the required data and logic to write a SQL INSERT query
    /// </summary>
    public class SqlInsertStructure
    {
        /// <summary>
        /// The name of the table the qeury will be applied on
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Columns in which values will be inserted
        /// </summary>
        public List<string> Columns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// Columns which will be returned from the inserted row
        /// </summary>
        public List<string> ReturnColumns { get; }

        /// <summary>
        /// Parameters required to execute the query
        /// </summary>
        public Dictionary<string, object> Parameters { get; }

        /// <summary>
        /// Used to assign unique parameter names
        /// </summary>
        public IncrementingInteger Counter { get; }

        private readonly TableDefinition _tableDefinition;
        private readonly IQueryBuilder _queryBuilder;

        public SqlInsertStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams, IQueryBuilder queryBuilder)
        {
            TableName = tableName;
            Columns = new();
            Values = new();
            Parameters = new();
            Counter = new();

            _tableDefinition = tableDefinition;
            _queryBuilder = queryBuilder;

            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                Columns.Add(QuoteIdentifier(param.Key));

                string paramName = $"param{Counter.Next()}";
                Values.Add($"@{paramName}");
                Parameters.Add(paramName, param.Value);
            }

            // return primary key so the inserted row can be identified
            ReturnColumns = _tableDefinition.PrimaryKey.Select(primaryKey => QuoteIdentifier(primaryKey)).ToList();
        }

        /// <summary>
        /// QuoteIdentifier simply forwards to the QuoteIdentifier
        /// implementation of the querybuilder that this query structure uses.
        /// So it wrapse the string in double quotes for Postgres and square
        /// brackets for MSSQL.
        /// </summary>
        private string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Used to identify the columns in which to insert values
        /// INSERT INTO {TableName} {ColumnsSql} VALUES ...
        /// </summary>
        public string ColumnsSql()
        {
            return "(" + string.Join(", ", Columns) + ")";
        }

        /// <summary>
        /// Creates the SLQ code for the inserted values
        /// INSERT INTO ... VALUES {ValuesSql}
        /// </summary>
        public string ValuesSql()
        {
            return "(" + string.Join(", ", Values) + ")";
        }

        /// <summary>
        /// Returns quote identified column names seperated by commas
        /// Used by Postgres like
        /// INSET INTO ... VALUES ... RETURNING {ReturnColumnsSql}
        /// </summary>
        public string ReturnColumnsSql()
        {
            return string.Join(", ", ReturnColumns);
        }

        /// <summary>
        /// Converts the query structure to the actual query string.
        /// </summary>
        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }
    }

    ///<summary>
    /// Wraps all the required data and logic to write a SQL UPDATE query
    ///</summary>
    public class SqlUpdateStructure
    {
        /// <summary>
        /// The name of the table the qeury will be applied on
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Predicates used to select the row to be updated
        /// </summary>
        public List<string> Predicates { get; }

        /// <summary>
        /// Updates to be applied to selected row
        /// </summary>
        public List<string> UpdateOperations { get; }

        /// <summary>
        /// Columns which will be returned from the updated row
        /// </summary>
        public List<string> ReturnColumns { get; }

        /// <summary>
        /// Parameters required to execute the query
        /// </summary>
        public Dictionary<string, object> Parameters { get; }

        /// <summary>
        /// Used to assign unique parameter names
        /// </summary>
        public IncrementingInteger Counter { get; }

        private readonly TableDefinition _tableDefinition;
        private readonly IQueryBuilder _queryBuilder;

        public SqlUpdateStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams, IQueryBuilder queryBuilder)
        {
            TableName = tableName;
            Predicates = new();
            UpdateOperations = new();
            Parameters = new();
            Counter = new();

            _tableDefinition = tableDefinition;
            _queryBuilder = queryBuilder;

            List<string> primaryKeys = _tableDefinition.PrimaryKey;
            List<string> columns = _tableDefinition.Columns.Keys.ToList();
            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    Predicates.Add($"{QuoteIdentifier(param.Key)} = @{MakeParamWithValue(param.Value)}");
                }
                // use columns to determine values to edit
                else if (columns.Contains(param.Key))
                {
                    UpdateOperations.Add($"{QuoteIdentifier(param.Key)} = @{MakeParamWithValue(param.Value)}");
                }
            }

            if (UpdateOperations.Count == 0)
            {
                throw new UpdateMutationHasNoUpdatesException();
            }

            // return primary key so the updated row can be identified
            ReturnColumns = primaryKeys.Select(primaryKey => QuoteIdentifier(primaryKey)).ToList();
        }

        /// <summary>
        ///  Add parameter to Parameters and return the name associated it with it
        /// </summary>
        private string MakeParamWithValue(object value)
        {
            string paramName = $"param{Counter.Next()}";
            Parameters.Add(paramName, value);
            return paramName;
        }

        /// <summary>
        /// QuoteIdentifier simply forwards to the QuoteIdentifier
        /// implementation of the querybuilder that this query structure uses.
        /// So it wrapse the string in double quotes for Postgres and square
        /// brackets for MSSQL.
        /// </summary>
        private string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Create the SQL code which will indetify which rows will be updated
        /// UPDATE ... SET ... WHERE {PredicatesSql}
        /// </summary>
        public string PredicatesSql()
        {
            if (Predicates.Count == 0)
            {
                return "1 = 1";
            }

            return string.Join(" AND ", Predicates);
        }

        /// <summary>
        /// Create the SQL code which will define the updates applied to the rows
        /// UPDATE ... SET {SetOperationsSql} WHERE ...
        /// </summary>
        public string SetOperationsSql()
        {
            return string.Join(", ", UpdateOperations);
        }

        /// <summary>
        /// Returns quote identified column names seperated by commas
        /// Used by Postgres like
        /// UPDATE ... SET ... WHERE ... RETURNING {ReturnColumnsSql}
        /// </summary>
        public string ReturnColumnsSql()
        {
            return string.Join(", ", ReturnColumns);
        }

        /// <summary>
        /// Converts the query structure to the actual query string.
        /// </summary>
        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }
    }
}
