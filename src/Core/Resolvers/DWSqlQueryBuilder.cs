// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class for building DwSql queries.
    /// </summary>
    public class DwSqlQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private static DbCommandBuilder _builder = new SqlCommandBuilder();
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <summary>
        /// Builds the sql query that will return the json result for DW.
        /// Sample: Get title of all books.
        /// SELECT COALESCE('['+STRING_AGG('{"title":"' + STRING_ESCAPE(ISNULL(title,''),'json') + '"}',', ')+']','[]')
        /// FROM (
        ///     SELECT TOP 100 [table0].[title] AS [title]
        ///     FROM [dbo].[books] AS [table0]
        ///     WHERE 1 = 1 ORDER BY [table0].[id] ASC
        ///     ) AS [table0]
        /// </summary>
        public string Build(SqlQueryStructure structure)
        {
            return BuildAsJson(structure);
        }

        /// <summary>
        /// Builds the sql query that will return the json result for the sql query.
        /// </summary>
        /// <param name="structure">Sql query structure to build query on.</param>
        /// <param name="subQueryStructure">if this is a sub query executed under outerapply.</param>
        /// <returns></returns>
        private string BuildAsJson(SqlQueryStructure structure, bool subQueryStructure = false)
        {
            string columns = GenerateColumnsAsJson(structure, subQueryStructure);
            string fromSql = $"{BuildSqlQuery(structure)}";
            string query = $"SELECT {columns}"
                + $" FROM ({fromSql}) AS {QuoteIdentifier(structure.SourceAlias)}";
            return query;
        }

        /// <summary>
        /// Build internal sql query for DW.
        /// This will generate the query that will return results in sql.
        /// Results of this query will be jsonified and returned to the user.
        /// Sample: Get title, publishers and authors of a book.
        /// SELECT TOP 100 [table0].[title] AS[title],
        /// [table1_subq].[data] AS[publishers],
        /// COALESCE('['+[table7_subq].[data]+']', '[]')) AS[authors]
        /// FROM dbo_books AS[table0]
        /// OUTER APPLY(SubQuery generated by recursive call to build function, will create the _subq tables)
        /// </summary>
        private string BuildSqlQuery(SqlQueryStructure structure)
        {
            string dataIdent = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
            StringBuilder fromSql = new();

            fromSql.Append($"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier($"{structure.SourceAlias}")}{Build(structure.Joins)}");

            fromSql.Append(string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({BuildAsJson(x.Value, true)}) AS {QuoteIdentifier(x.Key)}({dataIdent})")));

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

        private static string GenerateColumnsAsJson(SqlQueryStructure structure, bool subQueryStructure = false)
        {
            string columns;
            StringBuilder stringAgg = new();
            int i = 0;
            // Iterate through all the columns and build the string_agg
            foreach (LabelledColumn column in structure.Columns)
            {
                // Generate the col value.
                bool subQueryColumn = structure.IsSubqueryColumn(column);
                string col_value = column.Label;
                string escapedLabel = column.Label.Replace("'", "''");

                // If the column is not a subquery column and is not a string, cast it to string
                if (!subQueryColumn && structure.GetColumnSystemType(column.ColumnName) != typeof(string))
                {
                    col_value = $"CAST([{col_value}] AS NVARCHAR(MAX))";

                    Type col_type = structure.GetColumnSystemType(column.ColumnName);

                    if (col_type == typeof(DateTime))
                    {
                        // Need to wrap datetime in quotes to ensure correct deserialization.
                        stringAgg.Append($"N\'\"{escapedLabel}\":\"\' + ISNULL(STRING_ESCAPE({col_value},'json'),'null') + \'\"\'+");
                    }
                    else if (col_type == typeof(Boolean))
                    {
                        stringAgg.Append($"N\'\"{escapedLabel}\":\' + ISNULL(IIF({col_value} = 1, 'true', 'false'),'null')");
                    }
                    else
                    {
                        // Create json. Example: "book.id": 1 would be a sample output.
                        stringAgg.Append($"N\'\"{escapedLabel}\":\' + ISNULL(STRING_ESCAPE({col_value},'json'),'null')");
                    }
                }
                else
                {
                    // Create json. Example: "book.title": "Title" would be a sample output.
                    stringAgg.Append($"N\'\"{escapedLabel}\":\' + ISNULL(\'\"\'+STRING_ESCAPE([{col_value}],'json')+\'\"\','null')");
                }

                i++;

                // Add comma if not last column. example: {"id":"1234","name":"Big Company"}
                // the below ensures there is a comma after id but not after name.
                if (i != structure.Columns.Count)
                {
                    stringAgg.Append("+\',\'+");
                }
            }

            columns = $"STRING_AGG(\'{{\'+{stringAgg}+\'}}\',', ')";
            if (structure.IsListQuery)
            {
                // Array wrappers if we are trying to get a list of objects.
                columns = $"COALESCE(\'[\'+{columns}+\']\',\'[]\')";
            }
            else if (!subQueryStructure)
            {
                // outer apply sub queries can return null as that will be stored in the json.
                // However, for the main query, we need to return an empty string if the result is null as the sql cant read the NULL
                columns = $"COALESCE({columns},\'\')";
            }

            return columns;
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";

            // Predicates by virtue of database policy for Create action.
            string dbPolicypredicates = JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Create));

            // Columns whose values are provided in the request body - to be inserted into the record.
            string insertColumns = Build(structure.InsertColumns);

            // Values to be inserted into the entity.
            string values = dbPolicypredicates.Equals(BASE_PREDICATE) ?
                $"VALUES ({string.Join(", ", structure.Values)});" : $"SELECT {insertColumns} FROM (VALUES({string.Join(", ", structure.Values)})) T({insertColumns}) WHERE {dbPolicypredicates};";

            // Final insert query to be executed against the database.
            StringBuilder insertQuery = new();
            insertQuery.Append($"INSERT INTO {tableName} ({insertColumns}) ");
            insertQuery.Append(values);

            return insertQuery.ToString();
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                       Build(structure.Predicates));

            StringBuilder updateQuery = new($"UPDATE {tableName} SET {Build(structure.UpdateOperations, ", ")} ");
            updateQuery.Append($"WHERE {predicates};");
            return updateQuery.ToString();
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Delete),
                       Build(structure.Predicates));

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"WHERE {predicates} ";
        }

        /// <inheritdoc />
        public string Build(SqlExecuteStructure structure)
        {
            return $"EXECUTE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                $"{BuildProcedureParameterList(structure.ProcedureParameters)}";
        }

        /// <inheritdoc />
        public string Build(SqlUpsertQueryStructure structure)
        {
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";

            // Predicates by virtue of PK.
            string pkPredicates = JoinPredicateStrings(Build(structure.Predicates));

            string updateOperations = Build(structure.UpdateOperations, ", ");
            string queryToGetCountOfRecordWithPK = $"SELECT COUNT(*) as {COUNT_ROWS_WITH_GIVEN_PK} FROM {tableName} WHERE {pkPredicates}";

            // Query to get the number of records with a given PK.
            string prefixQuery = $"DECLARE @ROWS_TO_UPDATE int;" +
                $"SET @ROWS_TO_UPDATE = ({queryToGetCountOfRecordWithPK}); " +
                $"{queryToGetCountOfRecordWithPK};";

            // Final query to be executed for the given PUT/PATCH operation.
            StringBuilder upsertQuery = new(prefixQuery);

            // Query to update record (if there exists one for given PK).
            StringBuilder updateQuery = new(
                $"IF @ROWS_TO_UPDATE = 1 " +
                $"BEGIN " +
                $"UPDATE {tableName} " +
                $"SET {updateOperations} ");

            // End the IF block.
            updateQuery.Append("END ");

            // Append the update query to upsert query.
            upsertQuery.Append(updateQuery);
            if (!structure.IsFallbackToUpdate)
            {
                // Append the conditional to check if the insert query is to be executed or not.
                // Insert is only attempted when no record exists corresponding to given PK.
                upsertQuery.Append("ELSE BEGIN ");

                // Columns which are assigned some value in the PUT/PATCH request.
                string insertColumns = Build(structure.InsertColumns);

                // Predicates added by virtue of database policy for create operation.
                string createPredicates = JoinPredicateStrings(structure.GetDbPolicyForOperation(EntityActionOperation.Create));

                // Query to insert record (if there exists none for given PK).
                StringBuilder insertQuery = new($"INSERT INTO {tableName} ({insertColumns}) ");

                // Query to fetch the column values to be inserted into the entity.
                string fetchColumnValuesQuery = BASE_PREDICATE.Equals(createPredicates) ?
                    $"VALUES({string.Join(", ", structure.Values)});" :
                    $"SELECT {insertColumns} FROM (VALUES({string.Join(", ", structure.Values)})) T({insertColumns}) WHERE {createPredicates};";

                // Append the values to be inserted to the insertQuery.
                insertQuery.Append(fetchColumnValuesQuery);

                // Append the insert query to the upsert query.
                upsertQuery.Append(insertQuery.ToString());

                // End the ELSE block.
                upsertQuery.Append("END");
            }

            return upsertQuery.ToString();
        }

        /// <summary>
        /// Add a JSON_QUERY wrapper on the column
        /// </summary>
        private string WrapSubqueryColumn(LabelledColumn column, SqlQueryStructure subquery)
        {
            string builtColumn = Build(column as Column);
            if (subquery.IsListQuery)
            {
                return $"(COALESCE({builtColumn}, '[]'))";
            }

            return $"({builtColumn})";
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

        /// <inheritdoc />
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            string query = $"EXEC sp_describe_first_result_set @tsql = N'{databaseObjectName}';";
            return query;
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
        /// <returns>String representing the query needed to get a combined list of read only columns.</returns>
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

        /// <summary>
        /// Builds the parameter list for the stored procedure execute call
        /// paramKeys are the user-generated procedure parameter names
        /// paramValues are the auto-generated, parameterized values (@param0, @param1..)
        /// </summary>
        private static string BuildProcedureParameterList(Dictionary<string, object> procedureParameters)
        {
            StringBuilder sb = new();
            foreach ((string paramKey, object paramValue) in procedureParameters)
            {
                sb.Append($"@{paramKey} = {paramValue}, ");
            }

            string parameterList = sb.ToString();
            // If at least one parameter added, remove trailing comma and space, else return empty string
            return parameterList.Length > 0 ? parameterList[..^2] : parameterList;
        }
    }
}
