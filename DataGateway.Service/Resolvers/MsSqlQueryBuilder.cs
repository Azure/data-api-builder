using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Microsoft.Data.SqlClient;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Class for building MsSql queries.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        private static DbCommandBuilder _builder = new SqlCommandBuilder();

        public string DataIdent { get; } = "[data]";

        /// <summary>
        /// Enclose the given string within [] specific for MsSql.
        /// </summary>
        /// <param name="ident">The unquoted identifier to be enclosed.</param>
        /// <returns>The quoted identifier.</returns>
        public string QuoteIdentifier(string ident)
        {
            return _builder.QuoteIdentifier(ident);
        }

        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery)
        {
            if (subquery.IsListQuery)
            {
                return $"JSON_QUERY (COALESCE({column}, '[]'))";
            }

            return $"JSON_QUERY ({column})";
        }

        public string Build(SqlQueryStructure structure)
        {
            string fromSql = structure.TableSql();
            fromSql += string.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({DataIdent})"));
            string query = $"SELECT TOP {structure.Limit()} {structure.ColumnsSql()}"
                + $" FROM {fromSql}"
                + $" WHERE {structure.PredicatesSql()}"
                + $" ORDER BY {structure.OrderBySql()}";

            query += FOR_JSON_SUFFIX;
            if (!structure.IsListQuery)
            {
                query += "," + WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return query;
        }

        public string Build(SqlInsertStructure structure)
        {

            return $"INSERT INTO {QuoteIdentifier(structure.TableName)} {structure.ColumnsSql()} " +
                    $"OUTPUT {MakeOutputColumns(structure.ReturnColumns, OutputQualifier.Inserted)} " +
                    $"VALUES {structure.ValuesSql()};";
        }

        public string Build(SqlUpdateStructure structure)
        {
            return $"UPDATE {QuoteIdentifier(structure.TableName)} " +
                    $"SET {structure.SetOperationsSql()} " +
                    $"OUTPUT {MakeOutputColumns(structure.ReturnColumns, OutputQualifier.Inserted)} " +
                    $"WHERE {structure.PredicatesSql()};";
        }

        /// <summary>
        /// Labels with which columns can be marked in the OUTPUT clause
        /// </summary>
        private enum OutputQualifier { Inserted, Deleted };

        /// <summary>
        /// Adds qualifiers (inserted or deleted) to columns in OUTPUT clause and joins them with commas.
        /// e.g. for columns [C1, C2, C3] and output qualifier Inserted
        /// return Inserted.C1, Inserted.C2, Inserted.C3
        /// </summary>
        private static string MakeOutputColumns(List<string> columns, OutputQualifier outputQualifier)
        {
            List<string> outputColumns = columns.Select(column => $"{outputQualifier}.{column}").ToList();
            return string.Join(", ", outputColumns);
        }

        public string MakeKeysetPaginationPredicate(List<string> primaryKey, List<string> pkValues)
        {
            if (primaryKey.Count > 1)
            {
                StringBuilder result = new("(");
                for (int i = 0; i < primaryKey.Count; i++)
                {
                    if (i > 0)
                    {
                        result.Append(" OR ");
                    }

                    result.Append($"({MakePaginationInequality(primaryKey, pkValues, i)})");
                }

                result.Append(")");

                return result.ToString();
            }
            else
            {
                return MakePaginationInequality(primaryKey, pkValues, 0);
            }
        }

        /// <summary>
        /// Create an inequality where all primary key columns up to untilIndex are equilized to the
        /// respective pkValue, and the primary key colum at untilIndex has to be greater than its pkValue
        /// E.g. for
        /// primaryKey: [a, b, c, d, e, f]
        /// pkValues: [A, B, C, D, E, F]
        /// untilIndex: 2
        /// generate <c>a = A AND b = B AND c > C</c>
        /// </summary>
        private static string MakePaginationInequality(List<string> primaryKey, List<string> pkValues, int untilIndex)
        {
            string result = string.Empty;
            for (int i = 0; i <= untilIndex; i++)
            {
                string op = i == untilIndex ? ">" : "=";
                result += $"{primaryKey[i]} {op} {pkValues[i]}";

                if (i < untilIndex)
                {
                    result += " AND ";
                }
            }

            return result;
        }
    }
}
