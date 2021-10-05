using System;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MSSQL.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string x_ForJsonSuffix = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string x_WithoutArrayWrapperSuffix = "WITHOUT_ARRAY_WRAPPER";

        private static DbCommandBuilder Builder = new SqlCommandBuilder();
        public string QuoteIdentifier(string ident)
        {
            return Builder.QuoteIdentifier(ident);
        }

        public string WrapSubqueryColumn(string column, SqlQueryStructure subquery)
        {
            if (subquery.IsList())
            {
                return $"JSON_QUERY (COALESCE({column}, '[]'))";
            }
            return $"JSON_QUERY ({column})";
        }

        public string Build(SqlQueryStructure structure)
        {
            var selectedColumns = String.Join(", ", structure.Columns.Select(x => $"{x.Value} AS {QuoteIdentifier(x.Key)}"));
            string fromPart = structure.Table(structure.TableName, structure.TableAlias);
            fromPart += String.Join(
                    "",
                    structure.JoinQueries.Select(
                        x => $" OUTER APPLY ({Build(x.Value)}) AS {QuoteIdentifier(x.Key)}({structure.DataIdent})"));
            string query = $"SELECT {selectedColumns} FROM {fromPart}";
            if (structure.Conditions.Count() > 0)
            {
                query += $" WHERE {String.Join(" AND ", structure.Conditions)}";
            }
            query += x_ForJsonSuffix;
            if (!structure.IsList())
            {
                query += "," + x_WithoutArrayWrapperSuffix;
            }
            return query;
        }
    }
}
