// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using static Azure.DataApiBuilder.Core.Resolvers.CosmosQueryStructure;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    public class CosmosQueryBuilder : BaseSqlQueryBuilder
    {
        private readonly string _containerAlias = "c";

        /// <summary>
        /// Builds a cosmos sql query string
        /// </summary>
        /// <param name="structure"></param>
        /// <returns></returns>
        public string Build(CosmosQueryStructure structure)
        {
            StringBuilder queryStringBuilder = new();
            queryStringBuilder.Append($"SELECT {WrappedColumns(structure)}"
                + $" FROM {_containerAlias}");
            string predicateString = Build(structure.Predicates);

            if (structure.Joins != null && structure.Joins.Count > 0)
            {
                queryStringBuilder.Append($" {Build(structure.Joins)}");
            }

            structure.DbPolicyPredicatesForOperations.TryGetValue(EntityActionOperation.Read, out string? policy);
            // If there is a predicate or policy, add a WHERE clause
            if (!string.IsNullOrEmpty(predicateString) || !string.IsNullOrEmpty(policy))
            {
                queryStringBuilder
                    .Append(" WHERE ")
                    .Append(string.IsNullOrEmpty(predicateString) || string.IsNullOrEmpty(policy)
                                ? predicateString + policy
                                : string.Join(" AND ", predicateString, policy));
            }

            if (structure.OrderByColumns.Count > 0)
            {
                queryStringBuilder.Append($" ORDER BY {Build(structure.OrderByColumns)}");
            }

            return queryStringBuilder.ToString();
        }

        protected override string Build(Column column)
        {
            string alias = _containerAlias;
            if (column.TableAlias != null)
            {
                alias = column.TableAlias;
            }

            return alias + "." + column.ColumnName;
        }

        protected override string Build(KeysetPaginationPredicate? predicate)
        {
            // Cosmos doesnt do keyset pagination
            return string.Empty;
        }

        public override string QuoteIdentifier(string ident)
        {
            throw new System.NotImplementedException();
        }

        /// <summary>
        /// Build columns and wrap columns
        /// </summary>
        private string WrappedColumns(CosmosQueryStructure structure)
        {
            return string.Join(
                ", ",
                structure.Columns
                    .Select(c => _containerAlias + "." + c.Label)
                    .Distinct()
                );
        }

        /// <summary>
        /// Resolves a predicate operation enum to string
        /// </summary>
        protected override string Build(PredicateOperation op)
        {
            switch (op)
            {
                case PredicateOperation.Equal:
                    return "=";
                case PredicateOperation.GreaterThan:
                    return ">";
                case PredicateOperation.LessThan:
                    return "<";
                case PredicateOperation.GreaterThanOrEqual:
                    return ">=";
                case PredicateOperation.LessThanOrEqual:
                    return "<=";
                case PredicateOperation.NotEqual:
                    return "!=";
                case PredicateOperation.AND:
                    return "AND";
                case PredicateOperation.OR:
                    return "OR";
                case PredicateOperation.LIKE:
                    return "LIKE";
                case PredicateOperation.NOT_LIKE:
                    return "NOT LIKE";
                case PredicateOperation.IS:
                    return "";
                case PredicateOperation.IS_NOT:
                    return "NOT";
                default:
                    throw new ArgumentException($"Cannot build unknown predicate operation {op}.");
            }
        }

        /// <summary>
        /// Build left and right predicate operand and resolve the predicate operator into
        /// {OperandLeft} {Operator} {OperandRight}
        /// </summary>
        protected override string Build(Predicate? predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            string predicateString;
            if (ResolveOperand(predicate.Right).Equals(GQLFilterParser.NullStringValue))
            {
                predicateString = $" {Build(predicate.Op)} IS_NULL({ResolveOperand(predicate.Left)})";
            }
            else
            {
                predicateString = $"{ResolveOperand(predicate.Left)} {Build(predicate.Op)} {ResolveOperand(predicate.Right)} ";
            }

            if (predicate.AddParenthesis)
            {
                return "(" + predicateString + ")";
            }
            else
            {
                return predicateString;
            }
        }

        /// <summary>
        /// Build JOIN statements which will be used in the query.
        /// It makes sure that the same table is not joined multiple times by maintaining a set of table names.
        /// </summary>
        /// <param name="joinstructure"></param>
        /// <returns></returns>
        private static string Build(Stack<CosmosJoinStructure> joinstructure)
        {
            StringBuilder joinBuilder = new();

            HashSet<DatabaseObject> tableNames = new();
            foreach (CosmosJoinStructure structure in joinstructure)
            {
                if (tableNames.Contains(structure.DbObject))
                {
                    continue;
                }

                joinBuilder.Append($" JOIN {structure.TableAlias} IN {structure.DbObject.FullName}");
                tableNames.Add(structure.DbObject);
            }

            return joinBuilder.ToString();
        }

    }
}
