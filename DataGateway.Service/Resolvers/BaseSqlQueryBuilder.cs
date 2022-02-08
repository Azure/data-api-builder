using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Azure.DataGateway.Service.Models;

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
        protected abstract string Build(KeysetPaginationPredicate predicate);

        /// <summary>
        /// Build column as
        /// {TableAlias}.{ColumnName}
        /// If TableAlias is null
        /// {ColumnName}
        /// </summary>
        protected string Build(Column column)
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
            if (operand.AsColumn() != null)
            {
                return Build(operand.AsColumn());
            }
            else if (operand.AsString() != null)
            {
                return operand.AsString();
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
                default:
                    throw new ArgumentException($"Cannot build unknown predicate operation {op}.");
            }
        }

        /// <summary>
        /// Resolves a predicate logical operation enum to string
        /// </summary>
        protected string Build(LogicalOperation op)
        {
            switch (op)
            {
                case LogicalOperation.And:
                    return "AND";
                case LogicalOperation.Or:
                    return "OR";
                default:
                    throw new ArgumentException($"Cannot build unknown predicate logical operation {op}.");
            }
        }

        /// <summary>
        /// Build left and right predicate operand and resolve the predicate operator into
        /// {OperandLeft} {Operator} {OperandRight}
        /// </summary>
        protected string Build(Predicate predicate)
        {
            return $"{Build(predicate.Left)} {Build(predicate.Op)} {Build(predicate.Right)}";
        }

        /// <summary>
        /// Build and join predicates with logical op as seperator
        /// </summary>
        protected string Build(List<Predicate> predicates)
        {
            StringBuilder sb = new();
            foreach (Predicate p in predicates)
            {
                sb.Append(Build(p));
                if (p != predicates.Last<Predicate>())
                {
                    sb.Append($" {Build(p.Lop)} ");
                }
            }

            return sb.ToString();
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
        public string JoinPredicateStrings(params string[] predicateStrings)
        {
            IEnumerable<string> validPredicates = predicateStrings.Where(s => !string.IsNullOrEmpty(s));

            if (validPredicates.Count() == 0)
            {
                return "1 = 1";
            }

            return string.Join(" AND ", validPredicates);
        }
    }
}
