using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using static Azure.DataGateway.Service.Exceptions.DataGatewayException;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Base builder class for sql databases which contains shared
    /// methods for building query strucutres like Colum, LabelledColumn, Predicate etc
    /// </summary>
    public abstract class BaseSqlQueryBuilder
    {
        /// <summary>
        /// Adds database specific quotes to string identifier
        /// </summary>
        protected abstract string QuoteIdentifier(string ident);

        /// <summary>
        /// Builds a database specific keyset pagination predicate
        /// </summary>
        protected virtual string Build(KeysetPaginationPredicate? predicate)
        {
            if (predicate == null)
            {
                return string.Empty;
            }

            if (predicate.Columns.Count > 1)
            {
                StringBuilder result = new("(");
                for (int i = 0; i < predicate.Columns.Count; i++)
                {
                    if (i > 0)
                    {
                        result.Append(" OR ");
                    }

                    result.Append($"({MakePaginationInequality(predicate.Columns, predicate.Values, i)})");
                }

                result.Append(")");
                return result.ToString();
            }
            else
            {
                return MakePaginationInequality(predicate.Columns, predicate.Values, 0);
            }
        }

        /// <summary>
        /// Create an inequality where all columns up to untilIndex are equilized to the
        /// respective values, and the colum at untilIndex has to be compared to its Value
        /// E.g. for
        /// primaryKey: [a, b, c, d, e, f]
        /// pkValues: [A, B, C, D, E, F]
        /// untilIndex: 2
        /// generate <c>a = A AND b = B AND c > C</c>
        /// </summary>
        private string MakePaginationInequality(List<Column> columns, List<string> values, int untilIndex)
        {
            StringBuilder result = new();
            for (int i = 0; i <= untilIndex; i++)
            {
                string op;
                if (columns[i] is OrderByColumn)
                {
                    op = i == untilIndex ? GetComparisonFromDirection((columns[i] as OrderByColumn)!.Direction) : "=";
                    result.Append($"{Build(columns[i], printDirection: false)} {op} {values[i]}");
                }
                else
                {
                    op = i == untilIndex ? ">" : "=";
                    result.Append($"{Build(columns[i])} {op} {values[i]}");
                }

                if (i < untilIndex)
                {
                    result.Append(" AND ");
                }
            }

            return result.ToString();
        }

        /// <summary>
        /// Helper function returns the comparison operator appropriate
        /// for the given direction.
        /// </summary>
        /// <param name="direction">String represents direction.</param>
        /// <returns>Correct comparison operator.</returns>
        private static string GetComparisonFromDirection(string direction)
        {
            switch (direction)
            {
                case "Asc":
                    return ">";
                case "Desc":
                    return "<";
                default:
                    throw new DataGatewayException(message: $"Invalid sorting direction for pagination: {direction}",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Build column as
        /// {TableAlias}.{ColumnName}
        /// If TableAlias is null
        /// {ColumnName}
        /// If column is OrderByColumn
        /// and Direction is intended to be used,
        /// call Build with type OrderByColumn
        /// </summary>
        protected virtual string Build(Column column, bool printDirection = true)
        {
            if (printDirection && column is OrderByColumn)
            {
                return Build((column as OrderByColumn)!);
            }

            return Build(column);
        }

        /// <summary>
        /// Build column as
        /// {TableAlias}.{ColumnName}
        /// If TableAlias is null
        /// {ColumnName}
        /// </summary>
        protected virtual string Build(Column column)
        {
            if (column.TableAlias != null)
            {
                return QuoteIdentifier(column.TableAlias) + "." + QuoteIdentifier(column.ColumnName);
            }
            else
            {
                return QuoteIdentifier(column.ColumnName);
            }
        }

        /// <summary>
        /// Build column as
        /// {TableAlias}.{ColumnName} {direction}
        /// If TableAlias is null
        /// {ColumnName} {direction}
        /// </summary>
        protected virtual string Build(OrderByColumn column)
        {
            if (column.TableAlias != null)
            {
                return QuoteIdentifier(column.TableAlias) + "." + QuoteIdentifier(column.ColumnName) + " " + column.Direction;
            }
            else
            {
                return QuoteIdentifier(column.ColumnName) + " " + column.Direction;
            }
        }

        /// <summary>
        /// Build a labelled column as a column and attach
        /// ... AS {Label} to it
        /// </summary>
        protected string Build(LabelledColumn column)
        {
            return Build(column as Column) + " AS " + QuoteIdentifier(column.Label);
        }

        /// <summary>
        /// Build each column and join by ", " separator
        /// </summary>
        protected string Build(List<Column> columns)
        {
            return string.Join(", ", columns.Select(c => Build(c)));
        }

        /// <summary>
        /// Build each labelled column and join by ", " separator
        /// </summary>
        protected string Build(List<LabelledColumn> columns)
        {
            return string.Join(", ", columns.Select(c => Build(c)));
        }

        /// <summary>
        /// Builds the operand either as a column or returns it directly as string
        /// </summary>
        protected string Build(PredicateOperand operand)
        {
            if (operand == null)
            {
                throw new ArgumentNullException(nameof(operand));
            }

            Column? c;
            string? s;
            Predicate? p;
            if ((c = operand.AsColumn()) != null)
            {
                return Build(c);
            }
            else if ((s = operand.AsString()) != null)
            {
                return s;
            }
            else if ((p = operand.AsPredicate()) != null)
            {
                return Build(p);
            }
            else
            {
                throw new ArgumentException("Cannot get a value from PredicateOperand to build.");
            }
        }

        /// <summary>
        /// Resolves a predicate operation enum to string
        /// </summary>
        protected string Build(PredicateOperation op)
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
                default:
                    throw new ArgumentException($"Cannot build unknown predicate operation {op}.");
            }
        }

        /// <summary>
        /// Build left and right predicate operand and resolve the predicate operator into
        /// {OperandLeft} {Operator} {OperandRight}
        /// </summary>
        protected string Build(Predicate? predicate)
        {
            if (predicate is null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            string predicateString = $"{Build(predicate.Left)} {Build(predicate.Op)} {Build(predicate.Right)}";
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
        /// Build and join predicates with separator (" AND " by default)
        /// </summary>
        protected string Build(List<Predicate> predicates, string separator = " AND ")
        {
            return string.Join(separator, predicates.Select(p => Build(p)));
        }

        /// <summary>
        /// Write the join in sql
        /// INNER JOIN {TableName} AS {TableAlias} ON {JoinPredicates}
        /// </summary>
        protected string Build(SqlJoinStructure join)
        {
            if (join is null)
            {
                throw new ArgumentNullException(nameof(join));
            }

            return $" INNER JOIN {QuoteIdentifier(join.TableName)}"
                        + $" AS {QuoteIdentifier(join.TableAlias)}"
                        + $" ON {Build(join.Predicates)}";
        }

        /// <summary>
        /// Build and join each join with an empty separator
        /// </summary>
        protected string Build(List<SqlJoinStructure> joins)
        {
            return string.Join("", joins.Select(j => Build(j)));
        }

        /// <summary>
        /// Quote and join list of strings with a ", " separator
        /// </summary>
        protected string Build(List<string> columns)
        {
            return string.Join(", ", columns.Select(c => QuoteIdentifier(c)));
        }

        /// <summary>
        /// Join predicate strings while ignoring empty or null predicates
        /// </summary>
        /// <returns>returns "1 = 1" if no valid predicates</returns>
        public string JoinPredicateStrings(params string?[] predicateStrings)
        {
            IEnumerable<string> validPredicates = predicateStrings.Where(s => !string.IsNullOrEmpty(s)).Select(s => s!);

            if (!validPredicates.Any())
            {
                return "1 = 1";
            }

            return string.Join(" AND ", validPredicates);
        }
    }
}
