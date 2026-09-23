// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;

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
            return ident;
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
                case PredicateOperation.EXISTS:
                    return "EXISTS";
                case PredicateOperation.ARRAY_CONTAINS:
                    return "ARRAY_CONTAINS";
                case PredicateOperation.NOT_ARRAY_CONTAINS:
                    return "NOT ARRAY_CONTAINS";
                case PredicateOperation.IN:
                    return "IN";
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
            if (predicate.Left is not null)
            {
                if (predicate.Op == PredicateOperation.ARRAY_CONTAINS || predicate.Op == PredicateOperation.NOT_ARRAY_CONTAINS)
                {
                    predicateString = $" {Build(predicate.Op)} ( {ResolveOperand(predicate.Left)}, {ResolveOperand(predicate.Right)})";
                }
                else if (predicate.Op == PredicateOperation.ARRAY_SOME ||
                         predicate.Op == PredicateOperation.ARRAY_NONE ||
                         predicate.Op == PredicateOperation.ARRAY_ALL)
                {
                    string arrayField = ResolveOperand(predicate.Left);
                    string elementAlias = ArrayElementAlias(arrayField);

                    Predicate elementPredicate = predicate.Right?.AsPredicate()
                        ?? throw new ArgumentException("Array element filter (some/none/all) requires a nested predicate.");
                    string elementPredicateStr = BuildArrayElementPredicate(elementPredicate, elementAlias);

                    predicateString = predicate.Op switch
                    {
                        PredicateOperation.ARRAY_SOME => $" EXISTS(SELECT VALUE 1 FROM {elementAlias} IN {arrayField} WHERE {elementPredicateStr}) ",
                        PredicateOperation.ARRAY_NONE => $" NOT EXISTS(SELECT VALUE 1 FROM {elementAlias} IN {arrayField} WHERE {elementPredicateStr}) ",
                        // ARRAY_ALL: every element matches => no element exists that does not match.
                        _ => $" NOT EXISTS(SELECT VALUE 1 FROM {elementAlias} IN {arrayField} WHERE NOT ({elementPredicateStr})) "
                    };
                }
                else if (predicate.Op == PredicateOperation.ARRAY_ANY)
                {
                    string arrayField = ResolveOperand(predicate.Left);
                    predicateString = ResolveOperand(predicate.Right).Equals("true")
                        ? $" ARRAY_LENGTH({arrayField}) > 0 "
                        : $" (NOT IS_DEFINED({arrayField}) OR ARRAY_LENGTH({arrayField}) = 0) ";
                }
                else if (ResolveOperand(predicate.Right).Equals(GQLFilterParser.NullStringValue))
                {
                    // For Binary predicates:
                    predicateString = $" {Build(predicate.Op)} IS_NULL({ResolveOperand(predicate.Left)})";
                }
                else
                {
                    predicateString = $"{ResolveOperand(predicate.Left)} {Build(predicate.Op)} {ResolveOperand(predicate.Right)} ";
                }
            }
            else
            {
                // For Unary predicates, there is always a parenthesis around the operand.
                predicateString = $"{Build(predicate.Op)} ({ResolveOperand(predicate.Right)})";
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
        /// Generates a deterministic iteration alias for an array element based on the array field path,
        /// e.g. "c.tags" => "element_c_tags".
        /// </summary>
        private static string ArrayElementAlias(string arrayField)
            => "element_" + arrayField.Replace(".", "_").Replace("\"", string.Empty);

        /// <summary>
        /// Builds the predicate applied to each element of an array (used by some/none/all filters).
        /// The element alias is bound directly to the array column reference while walking the predicate
        /// tree, avoiding fragile string replacement on the rendered SQL.
        /// </summary>
        private string BuildArrayElementPredicate(Predicate predicate, string elementAlias)
        {
            // Leaf predicate: the left operand is the array column. The filter applies to the
            // element value itself, so render the element alias in place of the column reference.
            if (predicate.Left?.AsColumn() is not null)
            {
                string right = ResolveOperand(predicate.Right);
                return right.Equals(GQLFilterParser.NullStringValue)
                    ? $"{Build(predicate.Op)} IS_NULL({elementAlias})"
                    : $"{elementAlias} {Build(predicate.Op)} {right}";
            }

            // Chain predicate (AND/OR): recurse into both operands.
            string left = BuildArrayElementPredicate(predicate.Left!.AsPredicate()!, elementAlias);
            string rightStr = BuildArrayElementPredicate(predicate.Right!.AsPredicate()!, elementAlias);
            return $"({left} {Build(predicate.Op)} {rightStr})";
        }

        /// <summary>
        /// Resolves the operand either as a column, another predicate,
        /// a SqlQueryStructure or returns it directly as string
        /// </summary>
        protected new string ResolveOperand(PredicateOperand? operand)
        {
            if (operand == null)
            {
                throw new ArgumentNullException(nameof(operand));
            }

            Column? column;
            string? stringType;
            Predicate? predicate;
            BaseQueryStructure? sqlQueryStructure;
            if ((column = operand.AsColumn()) != null)
            {
                return Build(column);
            }
            else if ((stringType = operand.AsString()) != null)
            {
                return stringType;
            }
            else if ((predicate = operand.AsPredicate()) != null)
            {
                return Build(predicate);
            }
            else if ((sqlQueryStructure = operand.AsCosmosQueryStructure()) is not null
                        && sqlQueryStructure is CosmosExistsQueryStructure cosmosExistsQueryStructure)
            {
                return Build(cosmosExistsQueryStructure);
            }
            else if ((sqlQueryStructure = operand.AsCosmosQueryStructure()) is not null
                        && sqlQueryStructure is CosmosQueryStructure cosmosQueryStructure)
            {
                return Build(cosmosQueryStructure);
            }
            else
            {
                throw new ArgumentException("Cannot get a value from PredicateOperand to build.");
            }
        }

        /// <inheritdoc />
        public virtual string Build(CosmosExistsQueryStructure structure)
        {
            string query = $"SELECT 1 " +
                   $"FROM {QuoteIdentifier(structure.SourceAlias)} IN {QuoteIdentifier(structure.DatabaseObject.SchemaName)} " +
                   $"WHERE {Build(structure.Predicates)}";

            return query;
        }

        /// <summary>
        /// Generate Cosmos DB Query for the given fromClause and predicates.
        /// </summary>
        /// <param name="fromClause">Use to generate FROM part in sql along with table and JOINS</param>
        /// <param name="predicates">Query Conditions</param>
        /// <returns>CosmosDB Exist Query</returns>
        public static string BuildExistsQueryForCosmos(string? fromClause, string? predicates)
        {
            string? existQuery = $"EXISTS " +
                                $"(SELECT VALUE 1 " +
                                    $"FROM {fromClause} ";
            if (!string.IsNullOrEmpty(predicates))
            {
                existQuery += $"WHERE {predicates})";
            }
            else
            {
                existQuery += ")";
            }

            return existQuery;
        }
    }
}
