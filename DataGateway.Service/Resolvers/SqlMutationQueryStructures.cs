using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Services;

namespace Azure.DataGateway.Service.Resolvers
{
    ///<summary>
    /// Wraps all the required data and logic to write a SQL INSERT query
    ///</summary>
    public class SqlInsertStructure
    {
        public string TableName { get; }

        ///<summary>
        /// Columns in which values will be inserted
        ///</summary>
        public List<string> Columns { get; }

        /// <summary>
        /// Values to insert into the given columns
        /// </summary>
        public List<string> Values { get; }

        ///<summary>
        /// Columns which will be returned from the inserted row
        ///</summary>
        public List<string> ReturnColumns { get; }

        ///<summary>
        /// Parameters required to execute the query
        ///</summary>
        public Dictionary<string, object> Parameters { get; }

        ///<summary>
        /// Used to assign unique parameter names
        ///</summary>
        public IncrementingInteger Counter { get; }

        private readonly IQueryBuilder _queryBuilder;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public SqlInsertStructure(string tableName, IDictionary<string, object> mutationParams, IQueryBuilder queryBuilder, IMetadataStoreProvider metadataStoreProvider)
        {
            TableName = tableName;
            Columns = new();
            Values = new();
            Parameters = new();
            Counter = new();

            _queryBuilder = queryBuilder;
            _metadataStoreProvider = metadataStoreProvider;

            if (mutationParams.Count == 0)
            {
                throw new InsertMutationHasNoValuesException();
            }

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
            ReturnColumns = _metadataStoreProvider.GetTableDefinition(TableName).PrimaryKey.Select(primaryKey => QuoteIdentifier(primaryKey)).ToList();
        }

        private string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        public string ColumnsSql()
        {
            return "(" + string.Join(", ", Columns) + ")";
        }

        public string ValuesSql()
        {
            return "(" + string.Join(", ", Values) + ")";
        }

        public string ReturnColumnsSql()
        {
            return string.Join(", ", ReturnColumns);
        }

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
        public string TableName { get; }

        ///<summary>
        /// Predicates used to select the row to be updated
        ///</summary>
        public List<string> Predicates { get; }

        ///<summary>
        /// Updates to be applied to selected row
        ///</summary>
        public List<string> UpdateOperations { get; }

        ///<summary>
        /// Columns which will be returned from the updated row
        ///</summary>
        public List<string> ReturnColumns { get; }

        ///<summary>
        /// Parameters required to execute the query
        ///</summary>
        public Dictionary<string, object> Parameters { get; }

        ///<summary>
        /// Used to assign unique parameter names
        ///</summary>
        public IncrementingInteger Counter { get; }

        private readonly IQueryBuilder _queryBuilder;
        private readonly IMetadataStoreProvider _metadataStoreProvider;

        public SqlUpdateStructure(string tableName, IDictionary<string, object> mutationParams, IDictionary<string, string> fieldsToColumns, IQueryBuilder queryBuilder, IMetadataStoreProvider metadataStoreProvider)
        {
            TableName = tableName;
            Predicates = new();
            UpdateOperations = new();
            Parameters = new();
            Counter = new();

            _queryBuilder = queryBuilder;
            _metadataStoreProvider = metadataStoreProvider;

            List<string> primaryKeys = _metadataStoreProvider.GetTableDefinition(TableName).PrimaryKey;
            int primaryKeysUsedToSelectRows = 0;

            foreach (KeyValuePair<string, object> param in mutationParams)
            {
                if (param.Value == null)
                {
                    continue;
                }

                // primary keys used as predicates
                if (primaryKeys.Contains(param.Key))
                {
                    primaryKeysUsedToSelectRows++;
                    Predicates.Add($"{QuoteIdentifier(param.Key)} = @{MakeParamWithValue(param.Value)}");
                }
                // mapped parameters used as input for the update operations
                else if (fieldsToColumns.ContainsKey(param.Key))
                {
                    string tableColumnName;
                    fieldsToColumns.TryGetValue(param.Key, out tableColumnName);
                    UpdateOperations.Add($"{QuoteIdentifier(tableColumnName)} = @{MakeParamWithValue(param.Value)}");
                }
            }

            if (UpdateOperations.Count == 0)
            {
                throw new UpdateMutationHasNoUpdatesException();
            }

            // currently only allow modifying one entry at a time
            if (primaryKeysUsedToSelectRows < primaryKeys.Count)
            {
                throw new Exception("Not all primary keys have been specified for table under update. The query will affect multiple rows");
            }

            // return primary key so the updated row can be identified
            ReturnColumns = primaryKeys.Select(primaryKey => QuoteIdentifier(primaryKey)).ToList();
        }

        ///<summary>
        /// Add parameter to Parameters and return the name associated it with it
        ///</summary>
        private string MakeParamWithValue(object value)
        {
            string paramName = $"param{Counter.Next()}";
            Parameters.Add(paramName, value);
            return paramName;
        }

        private string QuoteIdentifier(string ident)
        {
            return _queryBuilder.QuoteIdentifier(ident);
        }

        public string PredicatesSql()
        {
            if (Predicates.Count == 0)
            {
                return "1 = 1";
            }

            return string.Join(" AND ", Predicates);
        }

        public string SetOperationsSql()
        {
            return string.Join(", ", UpdateOperations);
        }

        public string ReturnColumnsSql()
        {
            return string.Join(", ", ReturnColumns);
        }

        public override string ToString()
        {
            return _queryBuilder.Build(this);
        }
    }
}
