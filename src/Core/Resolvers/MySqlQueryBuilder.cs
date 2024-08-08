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

            if (structure.IsFallbackToUpdate)
            {
                return sets + ";\n" +
                    $"UPDATE {QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                        ", " + updates +
                    $" WHERE {Build(structure.Predicates)}; " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT " + select + $" WHERE @ROWCOUNT > 0;";
            }
            else
            {
                string insert = $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.Name)} ({Build(structure.InsertColumns)}) " +
                        $"VALUES ({string.Join(", ", (structure.Values))}) ";

                return sets + ";\n" +
                        insert + " ON DUPLICATE KEY " +
                        $"UPDATE {Build(structure.UpdateOperations, ", ")}" +
                        $", " + updates + ";" +
                        $" SET @ROWCOUNT=ROW_COUNT(); " +
                        $"SELECT " + select + $" WHERE @ROWCOUNT != 1;" +
                        $"SELECT {MakeUpsertSelections(structure)} WHERE @ROWCOUNT = 1;";
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
                    selections.Add($"{GetMySQLDefaultValue(columnDef)} as {quotedColName}");
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
    }
}
