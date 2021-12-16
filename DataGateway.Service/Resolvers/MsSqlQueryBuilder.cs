using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
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
        /// Converts OutputQualifier enums to strings
        /// </summary>
        private static string OutputQualifierResolver(OutputQualifier qualifier)
        {
            if (qualifier == OutputQualifier.Inserted)
            {
                return "Inserted";
            }
            else if (qualifier == OutputQualifier.Deleted)
            {
                return "Deleted";
            }
            else
            {
                throw new Exception("Could not determine output qualifier type");
            }
        }

        /// <summary>
        /// Adds qualifiers (inserted or deleted) to columns in OUTPUT clause and joins them will commas.
        /// </summary>
        private static string MakeOutputColumns(List<string> columns, OutputQualifier outputQualifier)
        {
            List<string> outputColumns = columns.Select(column => $"{OutputQualifierResolver(outputQualifier)}.{column}").ToList();
            return string.Join(", ", outputColumns);
        }
    }
}
