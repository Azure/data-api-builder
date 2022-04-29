using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MySqlConnector;

namespace Azure.DataGateway.Service.Resolvers
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
        protected override string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        /// <inheritdoc />
        public string Build(SqlQueryStructure structure)
        {
            string fromSql = $"{QuoteIdentifier(structure.TableName)} AS {QuoteIdentifier(structure.TableAlias)}{Build(structure.Joins)}";
            fromSql += string.Join("", structure.JoinQueries.Select(x => $" LEFT OUTER JOIN LATERAL ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)} ON TRUE"));

            string predicates = JoinPredicateStrings(
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
            return $"INSERT INTO {QuoteIdentifier(structure.TableName)} ({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}); " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT {MakeInsertSelections(structure)} WHERE @ROWCOUNT > 0;";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            (string sets, string updates, string select) = MakeStoreUpdatePK(structure.PrimaryKey());

            return sets + ";\n" +
                    $"UPDATE {QuoteIdentifier(structure.TableName)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                        ", " + updates +
                    $" WHERE {Build(structure.Predicates)}; " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT " + select + $" WHERE @ROWCOUNT > 0;";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            return $"DELETE FROM {QuoteIdentifier(structure.TableName)} " +
                    $"WHERE {Build(structure.Predicates)}";
        }

        /// <inheritdoc />
        public string Build(SqlUpsertQueryStructure structure)
        {
            (string sets, string updates, string select) = MakeStoreUpdatePK(structure.PrimaryKey());

            if (structure.IsFallbackToUpdate)
            {
                return sets + ";\n" +
                    $"UPDATE {QuoteIdentifier(structure.TableName)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                        ", " + updates +
                    $" WHERE {Build(structure.Predicates)}; " +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT " + select + $" WHERE @ROWCOUNT > 0;";
            }
            else
            {
                string insert = $"INSERT INTO {QuoteIdentifier(structure.TableName)} ({Build(structure.InsertColumns)}) " +
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
            string foreignKeyQuery = $@"
                SELECT 
                    CONSTRAINT_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition))}, 
                    TABLE_NAME {QuoteIdentifier(nameof(TableDefinition))}, 
                    COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencingColumns))}, 
                    REFERENCED_TABLE_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedTable))}, 
                    REFERENCED_COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedColumns))} 
                FROM 
                    INFORMATION_SCHEMA.KEY_COLUMN_USAGE 
                WHERE 
                    TABLE_SCHEMA IN (@{tableSchemaParamsForInClause}) 
                    AND TABLE_NAME IN (@{tableNameParamsForInClause}) 
                    AND REFERENCED_TABLE_NAME IS NOT NULL 
                    AND REFERENCED_COLUMN_NAME IS NOT NULL;";

            Console.WriteLine($"Foreign Key Query is : {foreignKeyQuery}");
            return foreignKeyQuery;
        }

        /// <summary>
        /// Makes the query segments to store PK during an update
        /// </summary>
        private (string, string, string) MakeStoreUpdatePK(List<string> primaryKey)
        {
            // Create local variables to store the pk columns
            string sets = String.Join(";\n", primaryKey.Select((x, index) => $"SET {"@LU_" + index.ToString()} := 0"));

            // Fetch the value to local variables
            string updates = String.Join(", ", primaryKey.Select((x, index) =>
                $"{QuoteIdentifier(x)} = (SELECT {"@LU_" + index.ToString()} := {QuoteIdentifier(x)})"));

            // Select local variables and mapping to original column name
            string select = String.Join(", ", primaryKey.Select((x, index) => $"{"@LU_" + index.ToString()} AS {QuoteIdentifier(x)}"));

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
                jsonColumns.Add($"{parametrizedCLabel}, {subqueryName}.{QuoteIdentifier(cLabel)}");
            }

            return string.Join(", ", jsonColumns);
        }

        /// <summary>
        /// Make the SELECT arguments to select the primary key of the last inserted element
        /// </summary>
        private string MakeInsertSelections(SqlInsertStructure structure)
        {
            List<string> selections = new();

            Dictionary<string, string> fields = new();

            int index = 0;
            foreach (string cols in structure.InsertColumns)
            {
                fields[cols] = structure.Values[index];
                Console.WriteLine(cols + "->" + fields[cols]);
                index++;
            }

            foreach (string column in structure.AllColumns())
            {
                ColumnDefinition columnDef = structure.GetColumnDefinition(column);

                string quotedColName = QuoteIdentifier(column);
                if (structure.InsertColumns.Contains(column))
                {
                    Console.WriteLine(column + "#->" + fields[column]);
                    selections.Add($"{fields[column]} as {quotedColName}");
                }
                else if (columnDef.IsAutoGenerated)
                {
                    //todo: this assumes one column pk
                    selections.Add($"last_insert_id() as {quotedColName}");
                }
                else if (columnDef.HasDefault)
                {
                    selections.Add($"{GetMySQLDefaultValue(columnDef)} as {quotedColName}");
                }
                else
                {
                    throw new UnsupportedContentTypeException($"Unsupported Column Definition: {columnDef}");
                }
            }

            return string.Join(", ", selections);
        }

        private string MakeUpsertSelections(SqlUpsertQueryStructure structure)
        {
            List<string> selections = new();

            List<string> fields = structure.AllColumns();

            int index = 0;
            foreach (string colName in fields)
            {
                string quotedColName = QuoteIdentifier(colName);

                if (structure.InsertColumns.Contains(colName))
                {
                    selections.Add($"{structure.Values[index]} AS {quotedColName}");
                    index++;
                }
                else if (structure.GetColumnDefinition(colName).IsAutoGenerated)
                {
                    selections.Add($"LAST_INSERT_ID() AS {quotedColName}");
                }
                else if (structure.GetColumnDefinition(colName).HasDefault)
                {
                    selections.Add($"{GetMySQLDefaultValue(structure.GetColumnDefinition(colName))} AS {quotedColName}");
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
            string defaultValue = column.DefaultValue.ToString()!;

            // HACK: Need to figure out how to proper parse the string with encoding
            if (defaultValue.StartsWith("_utf8mb4"))
            {
                defaultValue = defaultValue.Substring(8).Replace("\\'", "'");
            }

            return defaultValue;
        }
    }
}
