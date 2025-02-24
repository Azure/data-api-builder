// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.Data.SqlClient;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Class for building MsSql queries.
    /// </summary>
    public class MsSqlQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";
        private const string MSSQL_ESCAPE_CHAR = "\\";

        // Name of the column which stores the number of records with given PK. Used in Upsert queries.
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string dataIdent = QuoteIdentifier(SqlQueryStructure.DATA_IDENT);
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier($"{structure.SourceAlias}")}{Build(structure.Joins)}";

            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({dataIdent})"));

            string predicates;

            if (structure.IsMultipleCreateOperation)
            {
                predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates, " OR ", isMultipleCreateOperation: true),
                                    Build(structure.PaginationMetadata.PaginationPredicate));
            }
            else
            {
                predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));
            }

            // we add '\' character to escape the special characters in the string, but if special characters are needed to be searched
            // as literal characters we need to escape the '\' character itself. Since we add `\` only for LIKE, so we search if the query
            // contains LIKE and add the ESCAPE clause accordingly.
            predicates = AddEscapeToLikeClauses(predicates);

            string aggregations = string.Empty;
            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                if (structure.Columns.Any())
                {
                    aggregations = $",{BuildAggregationColumns(structure.GroupByMetadata)}";
                }
                else
                {
                    aggregations = $"{BuildAggregationColumns(structure.GroupByMetadata)}";
                }
            }

            string query = $"SELECT TOP {structure.Limit()} {WrappedColumns(structure)} {aggregations}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}";

            // Add GROUP BY clause if there are any group by columns
            if (structure.GroupByMetadata.Fields.Any())
            {
                query += $" GROUP BY {string.Join(", ", structure.GroupByMetadata.Fields.Values.Select(c => Build(c)))}";
            }

            if (structure.GroupByMetadata.Aggregations.Count > 0)
            {
                List<Predicate>? havingPredicates = structure.GroupByMetadata.Aggregations
                      .SelectMany(aggregation => aggregation.HavingPredicates ?? new List<Predicate>())
                      .ToList();

                if (havingPredicates.Any())
                {
                    query += $" HAVING {Build(havingPredicates)}";
                }
            }

            if (structure.OrderByColumns.Any())
            {
                query += $" ORDER BY {Build(structure.OrderByColumns)}";
            }

            query += FOR_JSON_SUFFIX;
            if (!structure.IsListQuery)
            {
                query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return query;
        }

        /// <summary>
        /// Builds the aggregation columns part of the SELECT clause
        /// </summary>
        private string BuildAggregationColumns(GroupByMetadata metadata)
        {
            return string.Join(", ", metadata.Aggregations.Select(aggregation => Build(aggregation.Column, useAlias: true)));
        }

        /// <summary>
        /// Helper method to add ESCAPE clause to the LIKE clauses in the query.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns></returns>
        private static string AddEscapeToLikeClauses(string predicate)
        {
            const string escapeClause = $" ESCAPE '{MSSQL_ESCAPE_CHAR}'";
            // Regex to find LIKE clauses and append ESCAPE
            return Regex.Replace(predicate, @"(LIKE\s+@[\w\d]+)", $"$1{escapeClause}", RegexOptions.IgnoreCase);
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
            bool isInsertDMLTriggerEnabled = sourceDefinition.IsInsertDMLTriggerEnabled;
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
            if (!isInsertDMLTriggerEnabled)
            {
                if (!string.IsNullOrEmpty(insertColumns))
                {
                    // When there is no DML trigger enabled on the table for insert operation, we can use OUTPUT clause to return the data.
                    insertQuery.Append($"INSERT INTO {tableName} ({insertColumns}) OUTPUT " +
                        $"{MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted.ToString())} ");
                    insertQuery.Append(values);
                }
                else
                {
                    insertQuery.Append($"INSERT INTO {tableName} OUTPUT " +
                        $"{MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted.ToString())} DEFAULT VALUES");
                }
            }
            else
            {
                // When a DML trigger for INSERT operation is enabled on the table, it's a bit tricky to get the inserted data.
                // We need to insert the values for all the non-autogenerated PK columns into a temporary table.
                // Finally this temporary table will be used to do a subsequent select on the actual table where we would join the
                // actual table and the temporary table based on the values of the non-autogenerated PK columns.
                // If there is a column in the PK which is autogenerated, we cannot and will not insert it into the temporary table.
                // Hence in the select query, for the autogenerated PK field, an additional WHERE predicate is added to fetch the unique record
                // by extracting the value of the autogenerated PK field using the SCOPE_IDENTITY() method provided by Sql Server.
                // It is to be noted that MsSql supports only one IDENTITY/autogenerated column per table.

                string tempTableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier($"#{structure.DatabaseObject.Name}_T")}";
                (string autoGenPKColumn, List<string> nonAutoGenPKColumns) = GetSegregatedPKColumns(sourceDefinition);
                if (nonAutoGenPKColumns.Count > 0)
                {
                    // Create temporary table containing zero rows and all the non-autogenerated columns present in the PK.
                    // We need to create it only when there are non-autogenerated columns present in the PK as column names.
                    // Using a 'WHERE 0=1' predicate ensures that we are not requesting back (and inserting) any additional data and hence no additional resources are consumed.
                    string queryToCreateTempTable = $"SELECT {string.Join(", ", nonAutoGenPKColumns.Select(pk => $"{QuoteIdentifier(pk)}"))}" +
                        $" INTO {tempTableName} FROM {tableName} WHERE 0 = 1;";

                    // We need to output values of all the non-autogenerated columns in the PK into the temporary table.
                    string nonAutoGenPKsOutput = string.Join(", ", nonAutoGenPKColumns.Select(pk => $"{OutputQualifier.Inserted}.{QuoteIdentifier(pk)}"));

                    // Creation of temporary table followed by inserting data into actual table.
                    insertQuery.Append(queryToCreateTempTable);
                    insertQuery.Append($"INSERT INTO {tableName} ({insertColumns}) ");
                    insertQuery.Append($"OUTPUT {nonAutoGenPKsOutput} INTO {tempTableName} ");
                }
                else
                {
                    insertQuery.Append($"INSERT INTO {tableName} ({insertColumns}) ");
                }

                insertQuery.Append(values);

                // Build the subsequent select query to return the inserted data. By the time the subsequent select executes,
                // the trigger would have already executed and we get the data as it is present in the table.
                StringBuilder subsequentSelect = new($"SELECT {MakeOutputColumns(structure.OutputColumns, tableName)} FROM {tableName} ");

                if (nonAutoGenPKColumns.Count > 0)
                {
                    // We will perform inner join on the basis of all the non-autogenerated columns in the PK.
                    string joinPredicates = string.Join(
                        "AND ",
                        nonAutoGenPKColumns.Select(pk => $"{tableName}.{QuoteIdentifier(pk)} = {tempTableName}.{QuoteIdentifier(pk)}"));
                    subsequentSelect.Append($"INNER JOIN {tempTableName} ON {joinPredicates} ");
                }

                if (!string.IsNullOrEmpty(autoGenPKColumn))
                {
                    // If there is an autogenerated column in the PK, we will add an additional WHERE condition for it.
                    // Using SCOPE_IDENTITY() method provided by sql server,
                    // we can get the last generated value of the autogenerated column.
                    subsequentSelect.Append($"WHERE {tableName}.{QuoteIdentifier(autoGenPKColumn)} = SCOPE_IDENTITY()");
                }

                insertQuery.Append(subsequentSelect.ToString());

                // Since we created a temporary table, it will be dropped automatically as the session terminates.
                // So, we don't need to explicitly drop the temporary table.
                insertQuery.Append(";");

                /* An example final insert query with trigger would look something like:
                 * -- Creation of temporary table
                 * SELECT [nonautogen_id] INTO [dbo].[#table_T] FROM [dbo].[table] WHERE 0 = 1;
                 * -- Insertion of values into the actual table
                 * INSERT INTO [dbo].[table] ([field1], [field2], [field3]) OUTPUT Inserted.[nonautogen_id] INTO [dbo].[#table_T]
                 * -- Values to insert into the table.
                 * VALUES(@param1, @param2, @param3);
                 * -- Subsequent select query to get back data.
                 * SELECT [dbo].[table].[id] AS [id], [dbo].[table].[nonautogen_id] AS [nonautogen_id], [dbo].[table].[field1] AS [field1], [dbo].[table].[field2] AS [field2], [dbo].[table].[field3] AS [field3]
                 * FROM [dbo].[table]
                 * -- INNER JOIN for non-autogen PK field
                 * INNER JOIN [dbo].[#table_T] ON [dbo].[table].[nonautogen_id] = [dbo].[#table_T].[nonautogen_id]
                 * -- WHERE clause for autogen PK field
                 * WHERE [dbo].[table].[autogen_id] = SCOPE_IDENTITY();
                 */
            }

            return insertQuery.ToString();
        }

        /// <summary>
        /// Helper method to get the autogenerated column in the PK and the non-autogenerated ones seperately.
        /// </summary>
        /// <param name="sourceDefinition">Table definition.</param>
        private static (string, List<string>) GetSegregatedPKColumns(SourceDefinition sourceDefinition)
        {
            string autoGenPKColumn = string.Empty;
            List<string> nonAutoGenPKColumns = new();
            foreach (string primaryKey in sourceDefinition.PrimaryKey)
            {
                if (sourceDefinition.Columns[primaryKey].IsAutoGenerated)
                {
                    autoGenPKColumn = primaryKey;
                }
                else
                {
                    nonAutoGenPKColumns.Add(primaryKey);
                }
            }

            return (autoGenPKColumn, nonAutoGenPKColumns);
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
            bool isUpdateTriggerEnabled = sourceDefinition.IsUpdateDMLTriggerEnabled;
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));
            string columnsToBeReturned =
                MakeOutputColumns(structure.OutputColumns, isUpdateTriggerEnabled ? string.Empty : OutputQualifier.Inserted.ToString());

            StringBuilder updateQuery = new($"UPDATE {tableName} SET {Build(structure.UpdateOperations, ", ")} ");

            // If a trigger is enabled on the entity, we cannot use OUTPUT clause to return the record.
            // In such a case, we will use a subsequent select query to get the record. By the time the subsequent select executes,
            // the trigger would have already executed and we get the data as it is present in the table.
            if (isUpdateTriggerEnabled)
            {
                updateQuery.Append($"WHERE {predicates};");
                updateQuery.Append($"SELECT {columnsToBeReturned} FROM {tableName} WHERE {predicates};");
                /* An example final update query on a table with update trigger enabled would look like:
                 *  UPDATE [dbo].[table] SET [dbo].[table].[field1] = @param3
                 *  -- predicate because of database policy
                 *  WHERE ([field3] != @param0) AND
                 *  -- predicates because of PK
                 *  [dbo].[table].[id] = @param1 AND [dbo].[table].[nonautogen_id] = @param2;
                 *  -- Subsequent select query.
                 *  SELECT [id] AS [id], [nonautogen_id] AS [nonautogen_id], [field1] AS [field1], [field2] AS [field2], [field3] AS [field3]
                 *  FROM [dbo].[table] WHERE ([field3] != @param0) AND [dbo].[table].[id] = @param1 AND [dbo].[table].[nonautogen_id] = @param2;
                 */
            }
            else
            {
                updateQuery.Append($"OUTPUT {columnsToBeReturned} WHERE {predicates};");
            }

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
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
            bool isUpdateTriggerEnabled = sourceDefinition.IsUpdateDMLTriggerEnabled;
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";

            // Predicates by virtue of PK.
            string pkPredicates = JoinPredicateStrings(Build(structure.Predicates));

            // Predicates by virtue of PK + database policy.
            string updatePredicates = JoinPredicateStrings(pkPredicates, structure.GetDbPolicyForOperation(EntityActionOperation.Update));

            string updateOperations = Build(structure.UpdateOperations, ", ");
            string columnsToBeReturned =
                MakeOutputColumns(structure.OutputColumns, isUpdateTriggerEnabled ? string.Empty : OutputQualifier.Inserted.ToString());
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

            if (isUpdateTriggerEnabled)
            {
                // If a trigger is enabled on the entity, we cannot use OUTPUT clause to return the record.
                // In such a case, we will use a subsequent select query to get the record. By the time the subsequent select executes,
                // the trigger would have already executed and we get the data as it is present in the table.
                updateQuery.Append($"WHERE {updatePredicates};");
                string subsequentSelect = $"SELECT {columnsToBeReturned} FROM {tableName} WHERE {updatePredicates};";
                updateQuery.Append(subsequentSelect);
            }
            else
            {
                updateQuery.Append($"OUTPUT {columnsToBeReturned} WHERE {updatePredicates};");
            }

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

                bool isInsertTriggerEnabled = sourceDefinition.IsInsertDMLTriggerEnabled;
                // We can only use OUTPUT clause to return inserted data when there is no trigger enabled on the entity.
                if (!isInsertTriggerEnabled)
                {
                    if (isUpdateTriggerEnabled)
                    {
                        // This is just an optimisation. If update trigger is enabled, then this build method had created
                        // columnsToBeReturned without the Inserted prefix.
                        columnsToBeReturned = MakeOutputColumns(structure.OutputColumns, OutputQualifier.Inserted.ToString());
                    }

                    insertQuery.Append($"OUTPUT {columnsToBeReturned}");
                }
                // If an insert trigger is enabled but there was no update trigger enabled,
                // we need to generate columnsToBeReturned without the 'Inserted' prefix on each column.
                else if (!isUpdateTriggerEnabled)
                {
                    // This is again just an optimisation. If update trigger was enabled, then the columnsToBeReturned would
                    // have already been created without any prefix.
                    columnsToBeReturned = MakeOutputColumns(structure.OutputColumns, string.Empty);
                }

                // Query to fetch the column values to be inserted into the entity.
                string fetchColumnValuesQuery = BASE_PREDICATE.Equals(createPredicates) ?
                    $"VALUES({string.Join(", ", structure.Values)});" :
                    $"SELECT {insertColumns} FROM (VALUES({string.Join(", ", structure.Values)})) T({insertColumns}) WHERE {createPredicates};";

                // Append the values to be inserted to the insertQuery.
                insertQuery.Append(fetchColumnValuesQuery);

                if (isInsertTriggerEnabled)
                {
                    // Since a trigger is enabled, a subsequent select query is to be executed to get the inserted record.
                    // By the time the subsequent select executes, the trigger would have already executed and we get the data as it is present in the table.
                    string subsequentSelect = $"SELECT {columnsToBeReturned} from {tableName} WHERE {pkPredicates};";
                    insertQuery.Append(subsequentSelect);
                }

                // Append the insert query to the upsert query.
                upsertQuery.Append(insertQuery.ToString());

                // End the ELSE block.
                upsertQuery.Append("END");
            }
            /* An example final upsert query on a table with update/insert triggers enabled would look like:
             * DECLARE @ROWS_TO_UPDATE int;
             * SET @ROWS_TO_UPDATE = (SELECT COUNT(*) as cnt_rows_to_update FROM [dbo].[table] WHERE [dbo].[table].[pkField1] = @param0 AND [dbo].[table].[pkField2] = @param1);
             * SELECT COUNT(*) as cnt_rows_to_update FROM [dbo].[table] WHERE [dbo].[table].[pkField1] = @param0 AND [dbo].[table].[pkField2] = @param1;
             * IF @ROWS_TO_UPDATE = 1
             * BEGIN UPDATE [dbo].[table]
             * SET [dbo].[table].[field3] = @param2, [dbo].[table].[field4] = @param3
             * WHERE [dbo].[table].[pkField1] = @param0 AND [dbo].[table].[pkField2] = @param1;
             * -- Subsequent select query.
             * SELECT [pkField1] AS [pkField1], [pkField2] AS [pkField2], [field3] AS [field3], [field4] AS [field4] FROM [dbo].[table]
             * WHERE [dbo].[table].[pkField1] = @param0 AND [dbo].[table].[pkField2] = @param1;
             * END
             * ELSE BEGIN
             * INSERT INTO [dbo].[table] ([pkField1], [pkField2], [field3]) VALUES(@param0, @param1, @param2);
             * -- Subsequent select query.
             * SELECT [pkField1] AS [pkField1], [pkField2] AS [pkField2], [field3] AS [field3], [field4] AS [field4] from [dbo].[table]
             * WHERE [dbo].[table].[pkField1] = @param0 AND [dbo].[table].[pkField2] = @param1;
             * END
             */

            return upsertQuery.ToString();
        }

        /// <summary>
        /// Labels with which columns can be marked in the OUTPUT clause
        /// </summary>
        private enum OutputQualifier { Inserted, Deleted };

        /// <summary>
        /// Adds qualifiers (inserted or deleted) to output columns in OUTPUT clause
        /// and joins them with commas. e.g. for outputcolumns [C1, C2, C3] and output
        /// qualifier Inserted return
        /// Inserted.ColumnName1 AS {Label1}, Inserted.ColumnName2 AS {Label2},
        /// Inserted.ColumnName3 AS {Label3}
        /// </summary>
        private string MakeOutputColumns(List<LabelledColumn> columns, string columnPrefix)
        {
            return string.Join(", ", columns.Select(c => Build(c, columnPrefix)));
        }

        /// <summary>
        /// Build a labelled column as a column and attach
        /// ... AS {Label} to it
        /// </summary>
        private string Build(LabelledColumn column, string columnPrefix)
        {
            if (string.IsNullOrEmpty(columnPrefix))
            {
                return $"{QuoteIdentifier(column.ColumnName)} AS {QuoteIdentifier(column.Label)}";
            }

            return $"{columnPrefix}.{QuoteIdentifier(column.ColumnName)} AS {QuoteIdentifier(column.Label)}";
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
            // The system type name column is aliased while the other columns are not to ensure
            // names are consistent across different sql implementations as all go through same deserialization logic
            string query = "SELECT " +
                            $"{STOREDPROC_COLUMN_NAME}, TYPE_NAME(system_type_id) as {STOREDPROC_COLUMN_SYSTEMTYPENAME}, {STOREDPROC_COLUMN_ISNULLABLE} " +
                            "FROM " +
                            "sys.dm_exec_describe_first_result_set_for_object (" +
                            $"OBJECT_ID('{databaseObjectName}'), 0) " +
                            "WHERE is_hidden is not NULL AND is_hidden = 0";
            return query;
        }

        /// <summary>
        /// Builds the query to get all the read-only columns in an MsSql table.
        /// For MsSql, the columns:
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
    }
}
