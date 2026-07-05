// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Npgsql;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private const string UPSERT_IDENTIFIER_COLUMN_NAME = "___upsert_op___";
        private const string INSERT_UPSERT = "inserted";
        private const string UPDATE_UPSERT = "updated";
        public const string COUNT_ROWS_WITH_GIVEN_PK = "cnt_rows_to_update";

        private static DbCommandBuilder _builder = new NpgsqlCommandBuilder();

        /// <inheritdoc />
        public override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string fromSql = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                             $"AS {QuoteIdentifier(structure.SourceAlias)}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));

            string predicates = JoinPredicateStrings(
                                    structure.GetDbPolicyForOperation(EntityActionOperation.Read),
                                    structure.FilterPredicates,
                                    Build(structure.Predicates),
                                    Build(structure.PaginationMetadata.PaginationPredicate));

            string query = $"SELECT {MakeSelectColumns(structure)}"
                + $" FROM {fromSql}"
                + $" WHERE {predicates}"
                + $" ORDER BY {Build(structure.OrderByColumns)}"
                + $" LIMIT {structure.Limit()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                result.Append($"SELECT COALESCE(jsonb_agg(to_jsonb({subqueryName})), '[]') ");
            }
            else
            {
                result.Append($"SELECT to_jsonb({subqueryName}) ");
            }

            result.Append($"AS {QuoteIdentifier(SqlQueryStructure.DATA_IDENT)} FROM ( ");
            result.Append(query);
            result.Append($" ) AS {subqueryName}");

            return result.ToString();
        }

        /// <inheritdoc />
        public string Build(SqlInsertStructure structure)
        {
            string insertQuery = $"INSERT INTO {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} ";
            if (structure.InsertColumns.Any())
            {
                insertQuery += $"({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}) ";
            }
            else
            {
                insertQuery += "DEFAULT VALUES ";
            }

            return $"{insertQuery} RETURNING {Build(structure.OutputColumns)}";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            string predicates = JoinPredicateStrings(
                                   structure.GetDbPolicyForOperation(EntityActionOperation.Update),
                                   Build(structure.Predicates));

            return $"UPDATE {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {predicates} " +
                    $"RETURNING {Build(structure.OutputColumns)};";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            string predicates = JoinPredicateStrings(
                       structure.GetDbPolicyForOperation(EntityActionOperation.Delete),
                       Build(structure.Predicates));

            return $"DELETE FROM {QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)} " +
                    $"WHERE {predicates}";
        }

        /// <summary>
        /// TODO; tracked here: https://github.com/Azure/hawaii-engine/issues/630
        /// </summary>
        public string Build(SqlExecuteStructure structure)
        {
            throw new NotImplementedException();
        }

        public string Build(SqlUpsertQueryStructure structure)
        {
            // https://stackoverflow.com/questions/42668720/check-if-postgres-query-inserted-or-updated-via-upsert
            // relying on xmax to detect insert vs update breaks for views
            string tableName = $"{QuoteIdentifier(structure.DatabaseObject.SchemaName)}.{QuoteIdentifier(structure.DatabaseObject.Name)}";
            string pkPredicates = Build(structure.Predicates);

            // RS1: COUNT of rows matching PK (no policy) — used to distinguish
            // "row doesn't exist" from "row exists but policy blocked" in the executor.
            string countQuery = $"SELECT COUNT(*) AS {COUNT_ROWS_WITH_GIVEN_PK} FROM {tableName} WHERE {pkPredicates}";

            string updatePredicates = JoinPredicateStrings(pkPredicates, structure.GetDbPolicyForOperation(EntityActionOperation.Update));
            string updateQuery = $"UPDATE {tableName} " +
                $"SET {Build(structure.UpdateOperations, ", ")} " +
                $"WHERE {updatePredicates} " +
                $"RETURNING {Build(structure.OutputColumns)}, '{UPDATE_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME}";

            if (structure.IsFallbackToUpdate)
            {
                // RS2: UPDATE only — no INSERT branch for autogen PK or missing required columns.
                return $"{countQuery}; {updateQuery};";
            }
            else
            {
                // INSERT only runs when row doesn't exist (pkPredicates match nothing)
                // AND the create policy (if any) is satisfied.
                string insertPredicates = JoinPredicateStrings(
                    $"NOT EXISTS (SELECT 1 FROM {tableName} WHERE {pkPredicates})",
                    structure.GetDbPolicyForOperation(EntityActionOperation.Create));

                // Alias each value with its column name so that policy predicates referencing
                // column names (e.g. "pieceid" != @param) can be resolved in the WHERE clause.
                // Using SELECT ... FROM (SELECT @p1 AS col1, ...) AS T avoids both the VALUES(NULL)
                // type inference issue and the unnamed-column resolution issue.
                string namedValues = string.Join(", ",
                    structure.InsertColumns.Zip(structure.Values,
                        (col, val) => $"{val} AS {QuoteIdentifier(col)}"));

                // RS2: CTE that attempts UPDATE first; falls through to INSERT only when row is absent.
                string cteQuery = $"WITH update_cte AS ( {updateQuery} ), insert_cte AS ( " +
                    $"INSERT INTO {tableName} ({Build(structure.InsertColumns)}) " +
                    $"SELECT {Build(structure.InsertColumns)} FROM (SELECT {namedValues}) AS T " +
                    $"WHERE {insertPredicates} " +
                    $"RETURNING {Build(structure.OutputColumns)}, '{INSERT_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME} ) " +
                    $"SELECT {BuildListOfLabels(structure.OutputColumns)}, {UPSERT_IDENTIFIER_COLUMN_NAME} FROM update_cte UNION ALL " +
                    $"SELECT {BuildListOfLabels(structure.OutputColumns)}, {UPSERT_IDENTIFIER_COLUMN_NAME} FROM insert_cte;";

                return $"{countQuery}; {cteQuery}";
            }
        }

        /// <summary>
        /// Build list of LabelledColumns as:
        /// "{label1}", "{label2}" ...
        /// </summary>
        private string BuildListOfLabels(List<LabelledColumn> labelledColumns)
        {
            return Build(labelledColumns.Select(labelledColumn => labelledColumn.Label).ToList());
        }

        /// <summary>
        /// Build column as
        /// "{tableAlias}"."{ColumnName}"
        /// or if SourceAlias is empty, as
        /// "{ColumnName}"
        /// </summary>
        protected override string Build(Column column)
        {
            // If the table alias is not empty, we return [{SourceAlias}].[{Column}]
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteIdentifier(column.TableAlias)}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias we return [{Column}]
            else
            {
                return $"{QuoteIdentifier(column.ColumnName)}";
            }
        }

        /// <summary>
        /// Looks into the upsert result returned by Postgres and returns
        /// whether the upsert was executed as an insert.
        /// This function also removes the metadata column that Postgres
        /// returns to indicate how UPSERT is executed.
        /// </summary>
        public static bool IsInsert(IDictionary<string, object?> upsertResult)
        {
            if (!upsertResult.ContainsKey(UPSERT_IDENTIFIER_COLUMN_NAME))
            {
                throw new ArgumentException($"Upsert result must have a {UPSERT_IDENTIFIER_COLUMN_NAME} column.");
            }

            object? opType = upsertResult[UPSERT_IDENTIFIER_COLUMN_NAME];
            upsertResult.Remove(UPSERT_IDENTIFIER_COLUMN_NAME);

            if (opType != null && opType is string opTypeStr)
            {
                switch (opTypeStr)
                {
                    case INSERT_UPSERT:
                        return true;
                    case UPDATE_UPSERT:
                        return false;
                }
            }

            throw new ArgumentException($"Invalid {UPSERT_IDENTIFIER_COLUMN_NAME} column value.");
        }

        /// <summary>
        /// Encode byte array columns to base64 strings instead of hex strings
        /// when parsing the results into json
        /// </summary>
        private string MakeSelectColumns(SqlQueryStructure structure)
        {
            List<string> builtColumns = new();

            // go through columns to find columns with type byte[]
            foreach (LabelledColumn column in structure.Columns)
            {
                // columns which contain the json of a nested type are called SqlQueryStructure.DATA_IDENT
                // and they are not actual columns of the underlying table so don't check for column type
                // in that scenario
                if (column.ColumnName != SqlQueryStructure.DATA_IDENT &&
                    structure.GetColumnSystemType(column.ColumnName) == typeof(byte[]))
                {
                    // postgres bytea is not stored as base64 so a convertion is made before
                    // producing the json result since HotChocolate handles ByteArray as base64
                    builtColumns.Add($"encode({Build(column as Column)}, 'base64') AS {QuoteIdentifier(column.Label)}");
                }
                else
                {
                    builtColumns.Add(Build(column));
                }
            }

            return string.Join(", ", builtColumns);
        }

        /// <inheritdoc/>
        public string BuildStoredProcedureResultDetailsQuery(string databaseObjectName)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc/>
        public string BuildQueryToGetReadOnlyColumns(string schemaParamName, string tableParamName)
        {
            string query = $"SELECT attname AS {QuoteIdentifier("COLUMN_NAME")} FROM pg_attribute " +
                $"WHERE attrelid = ({schemaParamName} || '.' || {tableParamName})::regclass AND attgenerated = 's';";
            return query;
        }

        public string QuoteTableNameAsDBConnectionParam(string param)
        {
            // PostreSQL uses same quoting for table name as DB Connection Param
            // as when used directly in SQL text.
            return QuoteIdentifier(param);
        }
    }
}
