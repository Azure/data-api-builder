// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Builds DAX queries from DaxQueryStructure for Semantic Models (Analysis Services).
    /// Generates EVALUATE statements with SELECTCOLUMNS, CALCULATETABLE, FILTER, TOPN, and ADDCOLUMNS.
    /// </summary>
    public class DaxQueryBuilder
    {
        /// <summary>
        /// Builds a DAX query string from the given DaxQueryStructure.
        /// </summary>
        /// <param name="structure">The query structure to build from.</param>
        /// <returns>A DAX query string.</returns>
        public static string Build(DaxQueryStructure structure)
        {
            StringBuilder query = new();
            string tableExpression = BuildTableExpression(structure);

            query.Append("EVALUATE");
            query.AppendLine();

            if (structure.TopCount.HasValue)
            {
                query.Append(BuildTopNExpression(structure, tableExpression));
            }
            else
            {
                query.Append(tableExpression);
            }

            if (structure.OrderByColumns.Count > 0)
            {
                query.AppendLine();
                query.Append(BuildOrderByClause(structure));
            }

            return query.ToString();
        }

        /// <summary>
        /// Builds the core table expression, including column selection, measures, and filters.
        /// </summary>
        private static string BuildTableExpression(DaxQueryStructure structure)
        {
            string baseTable = QuoteTableName(structure.TableName);
            string tableExpr = baseTable;

            // Apply filters via CALCULATETABLE if any
            if (structure.FilterPredicates.Count > 0)
            {
                string filters = string.Join(",\n    ", structure.FilterPredicates);
                tableExpr = $"CALCULATETABLE(\n    {baseTable},\n    {filters}\n)";
            }

            // Apply column selection via SELECTCOLUMNS
            bool hasColumnSelection = structure.SelectedColumns.Count > 0;
            bool hasMeasures = structure.IncludedMeasures.Count > 0;

            if (hasColumnSelection && hasMeasures)
            {
                // Use ADDCOLUMNS wrapping SELECTCOLUMNS to add measures
                string selectExpr = BuildSelectColumnsExpression(structure, tableExpr);
                string measureColumns = BuildMeasureColumns(structure);
                return $"ADDCOLUMNS(\n    {selectExpr},\n    {measureColumns}\n)";
            }
            else if (hasColumnSelection)
            {
                return BuildSelectColumnsExpression(structure, tableExpr);
            }
            else if (hasMeasures)
            {
                string measureColumns = BuildMeasureColumns(structure);
                return $"ADDCOLUMNS(\n    {tableExpr},\n    {measureColumns}\n)";
            }

            return tableExpr;
        }

        /// <summary>
        /// Builds a SELECTCOLUMNS expression for column projection.
        /// </summary>
        private static string BuildSelectColumnsExpression(DaxQueryStructure structure, string tableExpr)
        {
            StringBuilder sb = new();
            sb.Append("SELECTCOLUMNS(\n    ");
            sb.Append(tableExpr);

            foreach ((string alias, string originalName) in structure.SelectedColumns)
            {
                sb.Append($",\n    \"{alias}\", {QuoteTableName(structure.TableName)}{QuoteColumnName(originalName)}");
            }

            sb.Append("\n)");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the measure column arguments for ADDCOLUMNS.
        /// </summary>
        private static string BuildMeasureColumns(DaxQueryStructure structure)
        {
            List<string> parts = new();
            foreach (KeyValuePair<string, string> measure in structure.IncludedMeasures)
            {
                parts.Add($"\"{measure.Key}\", {measure.Value}");
            }

            return string.Join(",\n    ", parts);
        }

        /// <summary>
        /// Builds a TOPN expression for row limiting.
        /// </summary>
        private static string BuildTopNExpression(DaxQueryStructure structure, string tableExpr)
        {
            int topCount = structure.TopCount!.Value;
            bool usesBareColumns = structure.SelectedColumns.Count > 0;

            if (structure.OrderByColumns.Count > 0)
            {
                // TOPN with ordering.
                // When SELECTCOLUMNS is active, output columns don't have a table prefix,
                // so ORDER BY must use bare [column] references.
                List<string> orderParts = new();
                foreach ((string column, bool isAscending) in structure.OrderByColumns)
                {
                    string order = isAscending ? "ASC" : "DESC";
                    string colRef = usesBareColumns
                        ? QuoteColumnName(column)
                        : $"{QuoteTableName(structure.TableName)}{QuoteColumnName(column)}";
                    orderParts.Add($"{colRef}, {order}");
                }

                string orderBy = string.Join(", ", orderParts);
                return $"TOPN(\n    {topCount},\n    {tableExpr},\n    {orderBy}\n)";
            }

            return $"TOPN(\n    {topCount},\n    {tableExpr}\n)";
        }

        /// <summary>
        /// Builds the ORDER BY clause for the query.
        /// </summary>
        private static string BuildOrderByClause(DaxQueryStructure structure)
        {
            bool usesBareColumns = structure.SelectedColumns.Count > 0;
            List<string> parts = new();
            foreach ((string column, bool isAscending) in structure.OrderByColumns)
            {
                string order = isAscending ? "ASC" : "DESC";
                string colRef = usesBareColumns
                    ? QuoteColumnName(column)
                    : $"{QuoteTableName(structure.TableName)}{QuoteColumnName(column)}";
                parts.Add($"{colRef} {order}");
            }

            return $"ORDER BY {string.Join(", ", parts)}";
        }

        /// <summary>
        /// Quotes a table name for DAX (single quotes).
        /// </summary>
        public static string QuoteTableName(string tableName)
        {
            // DAX table names are enclosed in single quotes
            return $"'{tableName.Replace("'", "''")}'";
        }

        /// <summary>
        /// Quotes a column name for DAX (square brackets).
        /// </summary>
        public static string QuoteColumnName(string columnName)
        {
            // DAX column names are enclosed in square brackets
            return $"[{columnName.Replace("]", "]]")}]";
        }

        /// <summary>
        /// Builds a DAX filter predicate for a simple equality comparison.
        /// </summary>
        public static string BuildEqualityFilter(string tableName, string columnName, string parameterValue)
        {
            return $"{QuoteTableName(tableName)}{QuoteColumnName(columnName)} = {parameterValue}";
        }

        /// <summary>
        /// Builds a DAX filter predicate for a comparison operation.
        /// </summary>
        public static string BuildComparisonFilter(string tableName, string columnName, string op, string parameterValue)
        {
            return $"{QuoteTableName(tableName)}{QuoteColumnName(columnName)} {op} {parameterValue}";
        }
    }
}
