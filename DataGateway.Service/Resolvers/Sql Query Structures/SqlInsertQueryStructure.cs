using System.Collections.Generic;
using System.Linq;
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
}
