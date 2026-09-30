// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using MySqlConnector;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MySql
    /// </summary>
    public class MySqlQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private static DbCommandBuilder _builder = new MySqlCommandBuilder();
        public const string DATABASE_NAME_PARAM = "databaseName";

        /// <summary>
        /// Column alias for the upsert result indicator whose value is 1 when the target row existed
        /// before the upsert decision and 0 otherwise. Used by the query executor to distinguish an
        /// update from an insert and to detect database policy failures.
        /// </summary>
        public const string ROW_EXISTED_BEFORE_UPSERT = "row_existed_before_upsert";

        /// <summary>
        /// Adds database specific quotes to string identifier
        /// </summary>
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.Name)} AS {QuoteIdentifier(structure.SourceAlias)}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));

            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string query = $"SELECT {Build(structure.Columns)}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + $" ORDER BY {Build(structure.OrderByColumns)}"
                + $" LIMIT {structure.Limit()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                result.Append($"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT({MakeJsonObjectParams(structure, subqueryName)})), JSON_ARRAY()) ");
            }
            else
            {
                result.Append($"SELECT JSON_OBJECT({MakeJsonObjectParams(structure, subqueryName)}) ");
            }

            result.Append($"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} FROM ( ");
            result.Append(query);
            result.Append($" ) AS {subqueryName}");

            return result.ToString();
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            // No need to put into transaction as LAST_INSERT_ID is session level variable
            return $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.Name)} ({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}); " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT {MakeInsertSelections(structure)} WHERE @ROWCOUNT > 0;";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            (string sets, string updates, string select) = MakeQuerySegmentsForUpdate(structure, structure.OutputColumns);
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                       Build(structure.Predicates));

            return sets + ";\n" +
                    $"UPDATE {QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                        ", " + updates +
                    $" WHERE {predicates}; " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT " + select + $" WHERE @ROWCOUNT > 0;";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                    structure.GetDbPolicyForOperation(EntityActionOperation.Delete),
                    Build(structure.Predicates));

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"WHERE {predicates}";
        }

        /// <summary>
        /// TODO; tracked here: https://github.com/Azure/hawaii-engine/issues/630
        /// </summary>
        public string Build(SqlExecuteStructure structure)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public string Build(SqlUpsertQueryStructure structure)
        {
            (string sets, string updates, string select) = MakeQuerySegmentsForUpdate(structure, structure.OutputColumns);
            string tableName = QuoteIdentifier(structure.DatabaseObject.Name);

            // Predicates identifying the record by its primary key.
            string pkPredicates = Build(structure.Predicates);

            // Predicates for the UPDATE: primary key + database policy configured for the update operation.
            // Applying the update policy here ensures a PUT/PATCH cannot overwrite a record the caller is
            // not authorized to modify (e.g. a row owned by a different user).
            string updatePredicates = JoinPredicateStrings(
                pkPredicates,
                structure.GetDbPolicyForOperation(EntityActionOperation.Update));

            string updateOperations = Build(structure.UpdateOperations, ", ");

            if (structure.IsFallbackToUpdate)
            {
                // Update-only path (e.g. autogenerated primary key): no insert is attempted, so there is no
                // insert/update race to guard against.
                //  - @cnt: whether a record exists for the given primary key. Surfaced as the first result
                //    set so the executor can distinguish a policy failure (403) from a missing record (404).
                //  - @matched: whether a record exists that ALSO satisfies the update policy. The update
                //    output is emitted whenever @matched > 0 - regardless of whether any physical value
                //    actually changed - so that authorized idempotent updates still return the row (HTTP 200)
                //    even when the connection uses UseAffectedRows=true (where ROW_COUNT() would be 0).
                return sets + ";\n" +
                    $"SET @cnt := (SELECT COUNT(*) FROM {tableName} WHERE {pkPredicates}); " +
                    $"SET @matched := (SELECT COUNT(*) FROM {tableName} WHERE {updatePredicates}); " +
                    $"SELECT @cnt AS {QuoteIdentifier(ROW_EXISTED_BEFORE_UPSERT)}; " +
                    $"UPDATE {tableName} SET {updateOperations}, {updates} WHERE {updatePredicates}; " +
                    $"SELECT {select} WHERE @matched > 0;";
            }
            else
            {
                string insertColumns = Build(structure.InsertColumns);
                string insertValues = string.Join(", ", structure.Values);

                // The insert is expressed as INSERT ... ON DUPLICATE KEY UPDATE with a no-op update clause.
                // This makes the statement atomic and race-safe: concurrent upserts for the same missing
                // primary key serialize on the unique index here (one inserts, the others observe the
                // duplicate and no-op), rather than both reading an "absent" state and then deadlocking on
                // gap locks / failing with a duplicate-key error.
                //
                // The ON DUPLICATE branch runs only when the row already exists, so it is used to set
                // @existed := 1. Whether the row already existed is therefore detected by @existed - NOT by
                // ROW_COUNT() - because ROW_COUNT() for a no-op ON DUPLICATE KEY UPDATE is 1 (found) rather
                // than 0 when the connection reports found rows (UseAffectedRows=false, the default), which
                // would be indistinguishable from an insert. Setting the primary key column to itself keeps
                // the existing row unchanged, so the insert values never overwrite it and the update database
                // policy below cannot be bypassed.
                string firstPrimaryKey = QuoteIdentifier(structure.PrimaryKey().First());

                return sets + ";\n" +
                    $"SET @existed := 0; " +
                    $"INSERT INTO {tableName} ({insertColumns}) VALUES ({insertValues}) " +
                        $"ON DUPLICATE KEY UPDATE {firstPrimaryKey} = IF(@existed := 1, {firstPrimaryKey}, {firstPrimaryKey}); " +
                    // The row now exists (pre-existing or just inserted). @matched reflects whether the
                    // row satisfies the update policy; it is only consulted on the update path below.
                    $"SET @matched := (SELECT COUNT(*) FROM {tableName} WHERE {updatePredicates}); " +
                    // Surface whether the row already existed (1) or was inserted (0) as the first result set.
                    $"SELECT @existed AS {QuoteIdentifier(ROW_EXISTED_BEFORE_UPSERT)}; " +
                    // Apply the policy-aware update only when the row already existed.
                    $"UPDATE {tableName} SET {updateOperations}, {updates} WHERE @existed = 1 AND {updatePredicates}; " +
                    $"SELECT {select} WHERE @existed = 1 AND @matched > 0; " +
                    $"SELECT {MakeUpsertSelections(structure)} WHERE @existed = 0;";
            }
        }

        /// <inheritdoc />
        public override string BuildForeignKeyInfoQuery(int numberOfParameters)
        {
            string[] databaseNameParams = CreateParams(DATABASE_NAME_PARAM, numberOfParameters);
            string[] tableNameParams = CreateParams(TABLE_NAME_PARAM, numberOfParameters);
            string tableSchemaParamsForInClause = string.Join(", @", databaseNameParams);
            string tableNameParamsForInClause = string.Join(", @", tableNameParams);

            // For MySQL, the view KEY_COLUMN_USAGE provides all the information we need
            // so there is no need to join with any other view.
            // TABLE_SCHEMA returned here is actually the database name -
            // we don't need this column for MySql since the connection string already
            // has the database name. We still select it to conform with other dbs.
            string foreignKeyQuery = $@"
SELECT
    CONSTRAINT_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition))},
    TABLE_SCHEMA {QuoteIdentifier($"Referencing{nameof(DatabaseObject.SchemaName)}")},
    TABLE_NAME {QuoteIdentifier($"Referencing{nameof(SourceDefinition)}")},
    COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencingColumns))},
    REFERENCED_TABLE_SCHEMA {QuoteIdentifier($"Referenced{nameof(DatabaseObject.SchemaName)}")},
    REFERENCED_TABLE_NAME {QuoteIdentifier($"Referenced{nameof(SourceDefinition)}")},
    REFERENCED_COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedColumns))}
FROM
    INFORMATION_SCHEMA.KEY_COLUMN_USAGE
WHERE
    (TABLE_SCHEMA IN (@{tableSchemaParamsForInClause})
    AND TABLE_NAME IN (@{tableNameParamsForInClause})
    AND REFERENCED_TABLE_NAME IS NOT NULL
    AND REFERENCED_COLUMN_NAME IS NOT NULL) OR
    (REFERENCED_TABLE_SCHEMA IN (@{tableSchemaParamsForInClause}) AND
    REFERENCED_TABLE_NAME IN (@{tableNameParamsForInClause}))";

            return foreignKeyQuery;
        }

        /// <summary>
        /// Makes the query segments to store PK during an update. For each of the constructed segments, we do not include fields which are
        /// read-only because read-only fields cannot be included in an update statement as their value cannot be updated. And consequently,
        /// they cannot be included in the subsequent select statement as well.
        /// </summary>
        /// <param name="structure">Query structure of the update/upsert query.</param>
        /// <param name="outputColumns">List of columns to be returned.</param>
        /// <returns>A tuple of 3 strings where:
        /// 1. The first string is for the set clause: to create local variables to store the updatable columns.
        /// 2. The second string is for the update clause: to fetch the values of the updatable columns to local variables.
        /// 3. The third string is for the select clause: to select local variables and mapping to original column name.
        /// </returns>
        private (string, string, string) MakeQuerySegmentsForUpdate(BaseSqlQueryStructure structure, List<LabelledColumn> outputColumns)
        {
            SourceDefinition sourceDefinition = structure.GetUnderlyingSourceDefinition();
            List<string> columns = structure.AllColumns();

            // Create local variables to store the updatable columns.
            string sets = String.Join(";\n",
                columns.Where(col => !sourceDefinition.Columns[col].IsReadOnly || sourceDefinition.Columns[col].IsAutoGenerated)
                .Select((col, index) => $"SET {"@LU_" + index.ToString()} := 0"));

            // Fetch the values of the updatable columns to local variables.
            string updates = String.Join(", ",
                columns.Where(col => !sourceDefinition.Columns[col].IsReadOnly || sourceDefinition.Columns[col].IsAutoGenerated)
                .Select((col, index) => $"{QuoteIdentifier(col)} = (SELECT {"@LU_" + index.ToString()} := {QuoteIdentifier(col)})"));

            // Select local variables and mapping to original column name.
            string select = String.Join(", ",
                outputColumns.Where(col => !sourceDefinition.Columns[col.ColumnName].IsReadOnly || sourceDefinition.Columns[col.ColumnName].IsAutoGenerated)
                .Select((col, index) => $"{"@LU_" + index.ToString()} AS {QuoteIdentifier(col.Label)}"));
            /*
             * An example tuple of sets,updates, and select would look like:
             * sets:
             * SET @LU_0 := 0
             * SET @LU_1 := 0;
             * SET @LU_2 := 0
             * updates:
             * `param0` = (SELECT @LU_0 := `param0`), `param1` = (SELECT @LU_1 := `param1`), `param2` = (SELECT @LU_2 := `param2`)
             * select:
             * @LU_0 AS `param0`, @LU_1 AS `param1`, @LU_2 AS `param2`
             */

            return (sets, updates, select);
        }

        /// <summary>
        /// Makes the parameters for the JSON_OBJECT function from a list of labelled columns
        /// Format for table columns is:
        ///     "label1", subqueryName.label1, "label2", subqueryName.label2
        /// Format for subquery columns is:
        ///     "label1", JSON_EXTRACT(subqueryName.label1, '$'), "label2", JSON_EXTRACT(subqueryName.label2, '$')
        /// </summary>
        private string MakeJsonObjectParams(SqlQueryStructure structure, string subqueryName)
        {
            List<string> jsonColumns = new();
            foreach (LabelledColumn column in structure.Columns)
            {
                string cLabel = column.Label;
                string parametrizedCLabel = structure.ColumnLabelToParam[cLabel];

                // columns which contain the json of a nested type are called SqlQueryStructure.DATA_IDENT
                // and they are not actual columns of the underlying table so don't check for column type
                // in that scenario
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT &&
                    structure.GetColumnSystemType(column.ColumnName) == typeof(bool))
                {
                    // mysql does not resolve the boolean columns to true/false when converting to json, but to 1/0.
                    // In order to account for that, explicit casting is used.
                    // For more refer to: https://stackoverflow.com/questions/49131832/how-to-create-a-json-object-in-mysql-with-a-boolean-value
                    jsonColumns.Add($"{parametrizedCLabel}, CAST({subqueryName}.{QuoteIdentifier(cLabel)} is true as json)");
                }
                else if (column.ColumnName != SqlQueryStructure.DATA_IDENT &&
                    structure.GetColumnSystemType(column.ColumnName) == typeof(byte[]))
                {
                    jsonColumns.Add($"{parametrizedCLabel}, TO_BASE64({subqueryName}.{QuoteIdentifier(cLabel)})");
                }
                else
                {
                    jsonColumns.Add($"{parametrizedCLabel}, {subqueryName}.{QuoteIdentifier(cLabel)}");
                }
            }

            return string.Join(", ", jsonColumns);
        }

        /// <summary>
        /// Make the SELECT arguments to select the primary key of the last inserted element
        /// The SELECT clause looks for the inserted columns first, then Primary Key and then the Columns with Default values.
        /// For Example:book_id is the inserted column (book_id, id) are primary key, content has default value
        /// SELECT @param1 as `book_id`, last_insert_id() as `id`, @param0 as `content` WHERE @ROWCOUNT > 0;
        /// </summary>
        private string MakeInsertSelections(SqlInsertStructure structure)
        {
            List<string> selections = new();

            Dictionary<string, string> fields = new();

            int index = 0;
            foreach (string cols in structure.InsertColumns)
            {
                fields[cols] = structure.Values[index];
                index++;
            }

            foreach (LabelledColumn column in structure.OutputColumns)
            {
                ColumnDefinition columnDef = structure.GetColumnDefinition(column.ColumnName);

                string quotedColName = QuoteIdentifier(column.Label);
                if (structure.InsertColumns.Contains(column.ColumnName))
                {
                    selections.Add($"{fields[column.ColumnName]} as {quotedColName}");
                }
                else if (structure.PrimaryKey().Contains(column.ColumnName) && columnDef.IsAutoGenerated)
                {
                    //todo: this assumes one column pk
                    selections.Add($"last_insert_id() as {quotedColName}");
                }
                else if (columnDef.HasDefault)
                {
                    string columnSelectionValue = structure.InsertColumns.Any() ? GetMySQLDefaultValue(columnDef) : $"'{GetMySQLDefaultValue(columnDef)}'";
                    selections.Add($"{columnSelectionValue} as {quotedColName}");
                }
            }

            return string.Join(", ", selections);
        }

        private string MakeUpsertSelections(SqlUpsertQueryStructure structure)
        {
            List<string> selections = new();
            Dictionary<string, string> insertColumnsToParamName = structure.InsertColumns.Zip(structure.Values, (colName, paramName)
                => new { Key = colName, Value = paramName }).ToDictionary(kv => kv.Key, kv => kv.Value);

            List<LabelledColumn> fields = structure.OutputColumns;

            foreach (LabelledColumn column in fields)
            {
                string quotedColName = QuoteIdentifier(column.Label);
                ColumnDefinition columnDefinition = structure.GetColumnDefinition(column.ColumnName);
                if (columnDefinition.IsAutoGenerated)
                {
                    selections.Add($"LAST_INSERT_ID() AS {quotedColName}");
                }
                else if (columnDefinition.IsReadOnly)
                {
                    // We cannot update a read-only column and hence cannot include it in the response.
                    continue;
                }
                else if (insertColumnsToParamName.TryGetValue(column.ColumnName, out string? paramName))
                {
                    selections.Add($"{paramName} AS {quotedColName}");
                }
                else if (columnDefinition.HasDefault)
                {
                    selections.Add($"{GetMySQLDefaultValue(columnDefinition)} AS {quotedColName}");
                }
                else
                {
                    selections.Add($"NULL AS {quotedColName}");
                }
            }

            return string.Join(", ", selections);
        }

        private static string GetMySQLDefaultValue(ColumnDefinition column)
        {
            string defaultValue = column.DefaultValue!.ToString()!;

            // HACK: Need to figure out how to proper parse the string with encoding
            if (defaultValue.StartsWith("_utf8mb4"))
            {
                defaultValue = defaultValue.Substring(8).Replace("\\'", "'");
            }

            return defaultValue;
        }

        /// <inheritdoc/>
        public string BuildQueryToGetReadOnlyColumns(string schemaParamName, string tableParamName)
        {
            string query = "select COLUMN_NAME as COLUMN_NAME from INFORMATION_SCHEMA.COLUMNS " +
                $"where TABLE_SCHEMA = {schemaParamName} and TABLE_NAME = {tableParamName} and GENERATION_EXPRESSION != '';";
            return query;
        }

        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            throw new NotImplementedException();
        }

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // Table names in MySQL should not be quoted when used as DB Connection Params.
            return param;
        }
    }
}
