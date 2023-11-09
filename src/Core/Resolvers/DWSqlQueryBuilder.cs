// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class for building MsSql queries.
    /// </summary>
    public class DWQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string columns;
            string stringAgg = string.Empty;
            int i = 0;
            // Iterate through all the columns and build the string_agg
            foreach (LabelledColumn column in structure.Columns)
            {
                // Generate the col value.
                string col_value = Build(column as Column);

                // If the column is not a subquery column and is not a string, cast it to string
                if (!structure.IsSubqueryColumn(column) && structure.GetColumnSystemType(column.ColumnName) != typeof(string))
                {
                    col_value = $"CAST({col_value} AS NVARCHAR(MAX))";
                }

                // Create json. Example: "book.title": "Title" would be a sample output.
                stringAgg += $"\"{column.Label}\":\"\' + STRING_ESCAPE({col_value},'json') + \'\"";
                i++;

                // Add comma if not last column. example: {"id":"1234","name":"Big Company"}
                // the below ensures there is a comman after id but not after name.
                if (i != structure.Columns.Count)
                {
                    stringAgg += ",";
                }
            }

            if (structure.IsListQuery)
            {
                // Array wrappers if we are trying to get a list of objects.
                columns = $"COALESCE(\'[\'+STRING_AGG(\'{{{stringAgg}}}\',', ')+\']\',\'[]\')";
            }
            else
            {
                columns = $"STRING_AGG(\'{{{stringAgg}}}\',', ')";
            }

            // the fromSql generates the query that will return the actual results that will be jsonified
            // in format of earlier code.
            string fromSql = $"{BuildSqlQuery(structure)}";
            string query = $"SELECT {columns}"
                + $" FROM ({fromSql}) AS {QuoteIdentifier(structure.SourceAlias)}";
            return query;

        }

        /// <summary>
        /// Build internal query for DW.
        /// This will generate the query that will return results in sql.
        /// Results of this query will be jsonified and returned to the user.
        /// </summary>
        public string BuildSqlQuery(SqlQueryStructure structure)
        {
            string dataIdent = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier($"{structure.SourceAlias}")}{Build(structure.Joins)}";

            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({dataIdent})"));
            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));
            string columns = WrappedColumns(structure);
            string orderBy = $" ORDER BY {Build(structure.OrderByColumns)}";

            string query = $"SELECT TOP {structure.Limit()} {columns}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + orderBy;
            return query;
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            throw new NotImplementedException("DataWarehouse Sql currently does not support inserts");
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            throw new NotImplementedException("DataWarehouse sql currently does not support updates");
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            throw new NotImplementedException("DataWarehouse sql currently does not support deletes");
        }

        /// <inheritdoc />
        public string Build(SqlExecuteStructure structure)
        {
            throw new NotImplementedException("DataWarehouse sql currently does not support executes");
        }

        /// <inheritdoc />
        public string Build(SqlUpsertQueryStructure structure)
        {
            throw new NotImplementedException("DataWarehouse sql currently does not support updates");
        }

        /// <summary>
        /// Add a JSON_QUERY wrapper on the column
        /// </summary>
        private string WrapSubqueryColumn(LabelledColumn column, SqlQueryStructure subquery)
        {
            string builtColumn = Build(column as Column);
            if (subquery.IsListQuery)
            {
                return $"JSON_QUERY (COALESCE({builtColumn}, '[]'))";
            }

            return $"JSON_QUERY ({builtColumn})";
        }

        /// <summary>
        /// Build columns and wrap columns which represent join subqueries
        /// </summary>
        private string WrappedColumns(SqlQueryStructure structure)
        {
            return string.Join(", ",
                structure.Columns.Select(
                    c => structure.IsSubqueryColumn(c) ?
                        WrapSubqueryColumn(c, structure.JoinQueries[c.TableAlias!]) + $" AS {QuoteIdentifier(c.Label)}" :
                        Build(c)
            ));
        }

        /// <summary>
        /// Builds the query to fetch result set details of stored-procedure.
        /// result_field_name is the name of the result column.
        /// result_type contains the sql type, i.e char,int,varchar. Using TYPE_NAME method
        /// allows us to get the type without size constraints. example: TYPE_NAME for both
        /// varchar(100) and varchar(max) would be varchar.
        /// is_nullable is a boolean value to know if the result column is nullable or not.
        /// </summary>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            throw new NotImplementedException("DataWarehouse sql currently does not support stored procedures");
        }

        /// <summary>
        /// Builds the query to get all the read-only columns in an DWSql table.
        /// For DWSql, the columns:
        /// 1. That have data_type of 'timestamp', or
        /// 2. are computed based on other columns,
        /// are considered as read only columns. The query combines both the types of read-only columns and returns the list.
        /// </summary>
        /// <param name="schemaOrDatabaseParamName">Param name of the schema/database.</param>
        /// <param name="tableParamName">Param name of the table.</param>
        /// <returns></returns>
        public string BuildQueryToGetReadOnlyColumns(string schemaParamName, string tableParamName)
        {
            // For 'timestamp' columns sc.is_computed = 0.
            string query = "SELECT ifsc.column_name from sys.columns as sc INNER JOIN INFORMATION_SCHEMA.COLUMNS as ifsc " +
                "ON (sc.is_computed = 1 or ifsc.data_type = 'timestamp') " +
                $"AND sc.object_id = object_id({schemaParamName}+'.'+{tableParamName}) and ifsc.table_name = {tableParamName} " +
                $"AND ifsc.table_schema = {schemaParamName} and ifsc.column_name = sc.name;";

            return query;
        }

        /// <inheritdoc/>
        public string BuildFetchEnabledTriggersQuery()
        {
            string query = "SELECT STE.type_desc FROM sys.triggers ST inner join sys.trigger_events STE " +
                "On ST.object_id = STE.object_id AND ST.parent_id = object_id(@param0 + '.' + @param1) WHERE ST.is_disabled = 0;";

            return query;
        }
    }
}
