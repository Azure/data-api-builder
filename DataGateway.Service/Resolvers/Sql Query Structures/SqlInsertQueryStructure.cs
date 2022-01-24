using System.Collections.Generic;
using Azure.DataGateway.Service.Models;

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
        public List<string> InsertColumns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        /// <summary>
        /// The inserted columns that the insert will return
        /// </summary>
        public List<string> ReturnColumns { get; }

        private readonly TableDefinition _tableDefinition;

        public SqlInsertStructure(string tableName, TableDefinition tableDefinition, IDictionary<string, object> mutationParams)
        : base()
        {
            TableName = tableName;
            InsertColumns = new();
            Values = new();

            _tableDefinition = tableDefinition;
            ReturnColumns = _tableDefinition.PrimaryKey;

            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                InsertColumns.Add(param.Key);

                string paramName = $"param{Counter.Next()}";
                Values.Add($"@{paramName}");
                Parameters.Add(paramName, param.Value);
            }
        }
    }
}
