using System.Collections.Generic;
using Azure.DataGateway.Service.Models;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL DELETE query
    ///</summary>
    public class SqlDeleteStructure
    {
        /// <summary>
        /// The name of the table the qeury will be applied on
        /// </summary>
        public string TableName { get; }

        /// <summary>
        /// Predicates used to select the row to be deleted
        /// </summary>
        public List<string> Predicates { get; }

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

        public SqlDeleteStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams, IQueryBuilder queryBuilder)
        {
            TableName = tableName;
            Predicates = new();
            Parameters = new();
            Counter = new();

            _tableDefinition = tableDefinition;
            _queryBuilder = queryBuilder;

            List<string> primaryKeys = _tableDefinition.PrimaryKey;
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
            }
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
        /// Create the SQL code which will identify which rows will be deleted
        /// DELETE FROM ... WHERE {PredicatesSql}
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
        /// Converts the query structure to the actual query string.
        /// </summary>
        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }
    }
}
