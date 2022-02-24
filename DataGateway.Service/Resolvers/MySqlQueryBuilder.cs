using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Azure.DataGateway.Service.Models;
using MySqlConnector;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MySql
    /// </summary>
    public class MySqlQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private static DbCommandBuilder _builder = new MySqlCommandBuilder();

        /// <inheritdoc />
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
                + $" ORDER BY {Build(structure.PrimaryKeyAsColumns())}"
                + $" LIMIT {structure.Limit()}";

            string subqueryName = QuoteIdentifier($"subq{structure.Counter.Next()}");

            StringBuilder result = new();
            if (structure.IsListQuery)
            {
                result.Append($"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT({MakeJsonObjectParams(structure, subqueryName)})), '[]') ");
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
            // Create local variables to store the pk columns
            string sets = String.Join(";\n", structure.PrimaryKey().Select((x, index) => $"SET {"@LU_" + index.ToString()} := 0"));

            // Fetch the value to local variables
            string updates = String.Join(", ", structure.PrimaryKey().Select((x, index) =>
                $"{QuoteIdentifier(x)} = (SELECT {"@LU_" + index.ToString()} := {QuoteIdentifier(x)})"));

            // Select local variables and mapping to original column name
            string select = String.Join(", ", structure.PrimaryKey().Select((x, index) => $"{"@LU_" + index.ToString()} AS {QuoteIdentifier(x)}"));

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
            // Create local variables to store the pk columns
            string sets = String.Join(";\n", structure.PrimaryKey().Select((x, index) => $"SET {"@LU_" + index.ToString()} := 0"));

            // Fetch the value to local variables
            string updates = String.Join(", ", structure.PrimaryKey().Select((x, index) =>
                $"{QuoteIdentifier(x)} = (SELECT {"@LU_" + index.ToString()} := {QuoteIdentifier(x)})"));

            // Select local variables and mapping to original column name
            string select = String.Join(", ", structure.PrimaryKey().Select((x, index) => $"{"@LU_" + index.ToString()} AS {QuoteIdentifier(x)}"));

            string insert = $"INSERT INTO {QuoteIdentifier(structure.TableName)} ({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}) ";

            return sets + ";\n" +
                    insert + " ON DUPLICATE KEY " +
                    $"UPDATE {Build(structure.UpdateOperations, ", ")}" +
                    $", " + updates + ";" +
                    $" SET @ROWCOUNT=ROW_COUNT(); " +
                    $"SELECT " + select + $" WHERE @ROWCOUNT = 2;" +
                    $"SELECT {MakeUpsertSelections(structure, true)} WHERE @ROWCOUNT = 1;";
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
                if (structure.IsSubqueryColumn(column))
                {
                    jsonColumns.Add($"{parametrizedCLabel}, JSON_EXTRACT({subqueryName}.{QuoteIdentifier(cLabel)}, '$')");
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
        /// </summary>
        private string MakeInsertSelections(SqlInsertStructure structure)
        {
            List<string> selections = new();

            List<string> fields = structure.PrimaryKey()
                .Union(structure.InsertColumns).ToList();

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
                    //TODO: This assumes one column PK
                    selections.Add($"LAST_INSERT_ID() AS {quotedColName}");
                }
            }

            return string.Join(", ", selections);
        }

        private string MakeUpsertSelections(SqlUpsertQueryStructure structure, bool includePK)
        {
            List<string> selections = new();

            List<string> fields = structure.AllColumns();

            int index = 0;
            foreach (string colName in fields)
            {
                string quotedColName = QuoteIdentifier(colName);

                if (!includePK && structure.PrimaryKey().Contains(colName))
                {
                    continue;
                }

                if (structure.InsertColumns.Contains(colName))
                {
                    selections.Add($"{structure.Values[index]} AS {quotedColName}");
                    index++;
                }
                else if (structure.GetColumnDefinition(colName).IsAutoGenerated)
                {
                    selections.Add($"LAST_INSERT_ID() AS {quotedColName}");
                }
                else
                {
                    selections.Add($"NULL AS {quotedColName}");
                }
            }

            return string.Join(", ", selections);
        }
    }
}
