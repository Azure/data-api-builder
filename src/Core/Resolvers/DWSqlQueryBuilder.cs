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
    public class DwSqlQueryBuilder : BaseTSqlQueryBuilder, IQueryBuilder
    {
        private static DbCommandBuilder _builder = new SqlCommandBuilder();
        private readonly bool _enableNto1JoinOpt;
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";

        public DwSqlQueryBuilder(bool enableNto1JoinOpt = false)
        {
            // flag to enable the optimization for N to 1 join queries
            this._enableNto1JoinOpt = enableNto1JoinOpt;
        }

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
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>Generated sql queries based on the SqlQueryStructure</returns>
        public string Build(SqlQueryStructure structure)
        {
            if (this._enableNto1JoinOpt && HasToOneOrNoRelation(structure, false))
            {
                return BuildWithJsonFunc(structure, isSubQuery: false);
            }
            else
            {
                return BuildWithStringAgg(structure);
            }
        }

        /// <summary>
        /// Recursively checks the structure to see if
        /// 1. It only has to-1 relations
        /// 2. It does not have any relations, which means it is a simple query against one table
        /// We should apply the json funcs instead of string_agg for both cases
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <param name="isSubQuery">Used for recursive call purpose to generated different queries for sub-queries</param>
        /// <returns>True if the query structure only has N-1 relations or no relations at all</returns>
        private static bool HasToOneOrNoRelation(SqlQueryStructure structure, bool isSubQuery)
        {
            if (structure?.JoinQueries?.Values == null)
            {
                // if there is no sub-queries, use JSON PATH for performance improvements as well
                return true;
            }

            if (structure.IsListQuery && isSubQuery)
            {
                // If it is a list query in sub-query, then it is not a to-1 relation
                return false;
            }

            foreach (SqlQueryStructure subQueries in structure.JoinQueries.Values)
            {
                if (!HasToOneOrNoRelation(subQueries, true))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Build the query recursively with
        /// 1. JSON PATH for outer query
        /// 2. JSON OBJECT for inner query
        /// Example:
        /// SELECT TOP M 
        ///    <Columns>
        /// FROM
        ///    <Tables>
        /// OUTER APPLY
        ///    <Sub-query-tables>
        /// WHERE
        ///    <Conditions>
        /// ORDER BY
        ///    <Conditions>
        /// FOR JSON PATH, INCLUDE_NULL_VALUES;
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <param name="isSubQuery">Used for recursive call purpose to generated different queries for sub-queries</param>
        /// <returns>The sql queries built with json functions instead of string_agg</returns>
        private string BuildWithJsonFunc(SqlQueryStructure structure, bool isSubQuery)
        {
            StringBuilder query = new();

            if (isSubQuery)
            {
                // convert the columns to JSON Object for sub queries
                string columns = $"SELECT {GenerateColumnsAsJsonObject(structure)}";
                string fromSql = $" FROM ({BuildWithJsonFunc(structure)}) AS {QuoteIdentifier(structure.SourceAlias)}";

                query.Append(columns)
                    .Append(fromSql);
            }
            else
            {
                query.Append(BuildWithJsonFunc(structure))
                    .Append(BuildJsonPath(structure));
            }

            return query.ToString();
        }

        /// <summary>
        /// Helper function for BuildWithJsonFunc that generates "FROM" portion of the query
        /// </summary>
        /// <param name="structure">Sql query structure to build query on</param>
        /// <returns>The sql queries built with json functions instead of string_agg</returns>
        private string BuildWithJsonFunc(SqlQueryStructure structure)
        {
            string dataIdent = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier($"{structure.SourceAlias}")}{Build(structure.Joins)}";

            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({BuildWithJsonFunc(x.Value, true)}) AS {QuoteIdentifier(x.Key)}({dataIdent})"));

            string predicates = BuildPredicates(structure);

            string aggregations = BuildAggregationColumns(structure);

            StringBuilder query = new();

            query.Append($"SELECT TOP {structure.Limit()} {WrappedColumns(structure)} {aggregations}")
                .Append($" FROM {fromSql}")
                .Append($" WHERE {predicates}")
                .Append(BuildGroupBy(structure))
                .Append(BuildHaving(structure))
                .Append(BuildOrderBy(structure));

            return query.ToString();
        }

        /// <summary>
        /// Builds the sql query that will return the json result for the sql query using string_agg
        /// </summary>
        /// <param name="structure">Sql query structure to build query on.</param>
        /// <param name="subQueryStructure">if this is a sub query executed under outerapply.</param>
        /// <returns></returns>
        private string BuildWithStringAgg(SqlQueryStructure structure, bool subQueryStructure = false)
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
                        x => $" OUTER APPLY ({BuildWithStringAgg(x.Value, true)}) AS {QuoteIdentifier(x.Key)}({dataIdent})")));

            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string aggregations = string.Empty;
            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                if (structure.Columns.Any())
                {
                    aggregations = $", {BuildAggregationColumns(structure.GroupByMetadata)}";
                }
                else
                {
                    aggregations = $"{BuildAggregationColumns(structure.GroupByMetadata)}";
                }
            }

            StringBuilder queryBuilder = new();
            queryBuilder.Append($"SELECT TOP {structure.Limit()} {WrappedColumns(structure)} {aggregations}");
            queryBuilder.Append($" FROM {fromSql}");
            queryBuilder.Append($" WHERE {predicates}");

            // Add GROUP BY clause if there are any group by columns
            if (structure.GroupByMetadata.Fields.Any())
            {
                queryBuilder.Append($" GROUP BY {string.Join(", ", structure.GroupByMetadata.Fields.Values.Select(c => Build(c)))}");
            }

            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                List<Predicate> havingPredicates = structure.GroupByMetadata.Aggregations
                      .SelectMany(aggregation => aggregation.HavingPredicates ?? new List<Predicate>())
                      .ToList();

                if (havingPredicates.Any())
                {
                    queryBuilder.Append($" HAVING {Build(havingPredicates)}");
                }
            }

            if (structure.OrderByColumns.Any())
            {
                queryBuilder.Append($" ORDER BY {Build(structure.OrderByColumns)}");
            }

            string query = queryBuilder.ToString();

            return query;
        }

        /// <summary>
        /// Generate the columns selected and wrap them with JSON_OBJECT
        /// Example:
        /// SELECT JSON_OBJECT('id': [id]) 
        ///  FROM
        ///    (
        ///        SELECT TOP 1 
        ///            [table1].[id] AS [id]
        ///        FROM
        ///            [dbo].[book_website_placements] AS [table1]
        ///        WHERE
        ///            [table0].[id] = [table1].[book_id]
        ///            AND[table1].[book_id] = [table0].[id]
        ///        ORDER BY
        ///            [table1].[id] ASC
        ///    ) AS[table1]
        /// </summary>
        /// <param name="structure">Sql query structure to generate the columns with JSON_OBJECT</param>
        /// <returns></returns>
        private static string GenerateColumnsAsJsonObject(SqlQueryStructure structure)
        {
            List<string> columns = new();
            foreach (LabelledColumn column in structure.Columns)
            {
                string col_value = $"\'{column.Label}\': [{column.Label}]";
                columns.Add(col_value);
            }

            string joinedColumns = columns.Count > 1 ?
                 string.Join(",", columns) :
                 columns[0];

            return $"JSON_OBJECT({joinedColumns})";
        }

        private static string GenerateColumnsAsJson(SqlQueryStructure structure, bool subQueryStructure = false)
        {
            string columns;
            StringBuilder stringAgg = new();
            int columnCount = 0;
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
                    col_value = $"CONVERT(NVARCHAR(MAX), [{col_value}])";

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
                        stringAgg.Append($"{BuildJson(escapedLabel, col_value)},'null')");
                    }
                }
                else
                {
                    // Create json. Example: "book.title": "Title" would be a sample output.
                    stringAgg.Append($"N\'\"{escapedLabel}\":\' + ISNULL(\'\"\'+STRING_ESCAPE([{col_value}],'json')+\'\"\','null')");
                }

                columnCount++;

                // Add comma if not last column. example: {"id":"1234","name":"Big Company"}
                // the below ensures there is a comma after id but not after name.
                if (columnCount != structure.Columns.Count)
                {
                    stringAgg.Append("+\',\'+");
                }
            }

            int aggregationColumnCount = 0;
            // Handle aggregation columns
            foreach (AggregationOperation aggregation in structure.GroupByMetadata.Aggregations)
            {
                if (aggregationColumnCount == 0 && columnCount != 0)
                {
                    // need to add a comma if there are columns before the aggregation columns
                    stringAgg.Append("+\', \'+");
                }

                string col_value = aggregation.Column.OperationAlias;
                col_value = $"CONVERT(NVARCHAR(MAX), [{col_value}])";
                string escapedLabel = aggregation.Column.OperationAlias.Replace("'", "''");

                stringAgg.Append($"{BuildJson(escapedLabel, col_value)},'null')");

                aggregationColumnCount++;

                if (aggregationColumnCount != structure.GroupByMetadata.Aggregations.Count)
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

        private static string BuildJson(string escapedLabel, string col_value)
        {
            return $"N\'\"{escapedLabel}\":\' + ISNULL(STRING_ESCAPE({col_value},'json')";
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
            string query = "SELECT ifsc.COLUMN_NAME from sys.columns as sc INNER JOIN INFORMATION_SCHEMA.COLUMNS as ifsc " +
                "ON (sc.is_computed = 1 or ifsc.DATA_TYPE = 'timestamp') " +
                $"AND sc.object_id = object_id({schemaParamName}+'.'+{tableParamName}) AND ifsc.TABLE_SCHEMA = {schemaParamName} " +
                $"AND ifsc.TABLE_NAME = {tableParamName} AND ifsc.COLUMN_NAME = sc.name;";

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

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // Table names in DWSql should not be quoted when used as DB Connection Params.
            return param;
        }
    }
}
