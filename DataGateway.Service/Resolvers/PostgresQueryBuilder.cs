using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using Azure.DataGateway.Service.Models;
using Npgsql;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder : BaseSqlQueryBuilder, IQueryBuilder
    {
        private const string UPSERT_IDENTIFIER_COLUMN_NAME = "___upsert_op___";
        private const string INSERT_UPSERT = "inserted";
        private const string UPDATE_UPSERT = "updated";

        private static DbCommandBuilder _builder = new NpgsqlCommandBuilder();

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
            return $"INSERT INTO {QuoteIdentifier(structure.TableName)} ({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}) " +
                    $"RETURNING {Build(structure.ReturnColumns)};";
        }

        /// <inheritdoc />
        public string Build(SqlUpdateStructure structure)
        {
            return $"UPDATE {QuoteIdentifier(structure.TableName)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {Build(structure.Predicates)} " +
                    $"RETURNING {Build(structure.PrimaryKey())};";
        }

        /// <inheritdoc />
        public string Build(SqlDeleteStructure structure)
        {
            return $"DELETE FROM {QuoteIdentifier(structure.TableName)} " +
                    $"WHERE {Build(structure.Predicates)}";
        }

        public string Build(SqlUpsertQueryStructure structure)
        {
            if (structure.IsFallbackToUpdate)
            {
                return $"UPDATE {QuoteIdentifier(structure.TableName)} " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"WHERE {Build(structure.Predicates)} " +
                    $"RETURNING {Build(structure.ReturnColumns)}, '{UPDATE_UPSERT}' AS {UPSERT_IDENTIFIER_COLUMN_NAME};";
            }
            else
            {
                return $"INSERT INTO {QuoteIdentifier(structure.TableName)} ({Build(structure.InsertColumns)}) " +
                    $"VALUES ({string.Join(", ", (structure.Values))}) " +
                    $"ON CONFLICT ({Build(structure.PrimaryKey())}) DO UPDATE " +
                    $"SET {Build(structure.UpdateOperations, ", ")} " +
                    $"RETURNING {Build(structure.ReturnColumns)}, " +
                    $"case when xmax::text::int > 0 then '{UPDATE_UPSERT}' else '{INSERT_UPSERT}' end AS {UPSERT_IDENTIFIER_COLUMN_NAME};";
            }
        }

        /// <inheritdoc />
        protected override string Build(KeysetPaginationPredicate? predicate)
        {
            if (predicate == null)
            {
                return string.Empty;
            }

            string left = Build(predicate.PrimaryKey);
            string right = string.Join(", ", predicate.Values);

            if (predicate.PrimaryKey.Count > 1)
            {
                return $"({left}) > ({right})";
            }
            else
            {
                return $"{left} > {right}";
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
    }
}
