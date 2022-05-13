using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Azure.DataGateway.Config;
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
        public const string SCHEMA_NAME_PARAM = "schemaName";
        public const string TABLE_NAME_PARAM = "tableName";

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

                    result.Append($"({MakePaginationInequality(predicate.Columns, untilIndex: i)})");
                }

                result.Append(")");
                return result.ToString();
            }
            else
            {
                return MakePaginationInequality(predicate.Columns, untilIndex: 0);
            }
        }

        /// <summary>
        /// Create an inequality where all columns up to untilIndex are equilized to the
        /// respective values, and the column at untilIndex has to be compared to its Value
        /// E.g. for
        /// primaryKey: [a, b, c, d, e, f]
        /// pkValues: [A, B, C, D, E, F]
        /// untilIndex: 2
        /// generate <c>a = A AND b = B AND c > C</c>
        /// </summary>
        private string MakePaginationInequality(List<PaginationColumn> columns, int untilIndex)
        {
            StringBuilder result = new();
            for (int i = 0; i <= untilIndex; i++)
            {
                string op = i == untilIndex ? GetComparisonFromDirection(columns[i].Direction) : "=";
                result.Append($"{Build(columns[i], printDirection: false)} {op} {columns[i].ParamName}");

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
        private static string GetComparisonFromDirection(OrderByDir direction)
        {
            switch (direction)
            {
                case OrderByDir.Asc:
                    return ">";
                case OrderByDir.Desc:
                    return "<";
                default:
                    throw new DataGatewayException(message: $"Invalid sorting direction for pagination: {direction}",
                                                   statusCode: HttpStatusCode.BadRequest,
                                                   subStatusCode: SubStatusCodes.BadRequest);
            }
        }

        /// <summary>
        /// Build column as
        /// [{tableAlias}].[{ColumnName}]
        /// or if TableAlias is empty, as
        /// [{schema}].[{table}].[{ColumnName}]
        /// or if schema is empty, as
        /// [{table}].[{ColumnName}]
        /// </summary>
        protected virtual string Build(Column column)
        {
            // If the table alias is not empty, we return [{TableAlias}].[{Column}]
            if (!string.IsNullOrEmpty(column.TableAlias))
            {
                return $"{QuoteIdentifier(column.TableAlias)}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias then if the schema is not empty, we return [{TableSchema}].[{TableName}].[{Column}]
            else if (!string.IsNullOrEmpty(column.TableSchema))
            {
                return $"{QuoteIdentifier($"{column.TableSchema}")}.{QuoteIdentifier($"{column.TableName}")}.{QuoteIdentifier(column.ColumnName)}";
            }
            // If there is no table alias, and no schema, we return [{TableName}].[{Column}]
            else
            {
                return $"{QuoteIdentifier($"{column.TableName}")}.{QuoteIdentifier(column.ColumnName)}";
            }
        }

        /// <summary>
        /// Build orderby column as
        /// {TableAlias}.{ColumnName} {direction}
        /// If TableAlias is null
        /// {ColumnName} {direction}
        /// </summary>
        protected virtual string Build(OrderByColumn column, bool printDirection = true)
        {
            StringBuilder builder = new();
            builder.Append(Build(column as Column));
            return printDirection ? builder.Append(" " + column.Direction).ToString() : builder.ToString();
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
        /// Build each OrderByColumn and join by ", " separator
        /// </summary>
        protected string Build(List<OrderByColumn> columns)
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
        protected virtual string Build(PredicateOperation op)
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
                    return "IS";
                case PredicateOperation.IS_NOT:
                    return "IS NOT";
                default:
                    throw new ArgumentException($"Cannot build unknown predicate operation {op}.");
            }
        }

        /// <summary>
        /// Build left and right predicate operand and resolve the predicate operator into
        /// {OperandLeft} {Operator} {OperandRight}
        /// </summary>
        protected virtual string Build(Predicate? predicate)
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

        /// <inheritdoc />
        public virtual string BuildForeignKeyInfoQuery(int numberOfParameters)
        {
            string[] schemaNameParams =
                CreateParams(kindOfParam: SCHEMA_NAME_PARAM, numberOfParameters);

            string[] tableNameParams =
                CreateParams(kindOfParam: TABLE_NAME_PARAM, numberOfParameters);
            string tableSchemaParamsForInClause = string.Join(", @", schemaNameParams);
            string tableNameParamsForInClause = string.Join(", @", tableNameParams);

            // The view REFERENTIAL_CONSTRAINTS has a row for each referential key CONSTRAINT_NAME and
            // its corresponding UNIQUE_CONSTRAINT_NAME to which it references.
            // These are only constraint names so we need to join with the view KEY_COLUMN_USAGE to get the
            // constraint columns - one inner join for the columns from the 'Referencing table'
            // and the other join for the columns from the 'Referenced Table'.
            string foreignKeyQuery = $@"
                SELECT 
                    ReferentialConstraints.CONSTRAINT_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition))}, 
                    ReferencingColumnUsage.TABLE_NAME {QuoteIdentifier(nameof(TableDefinition))}, 
                    ReferencingColumnUsage.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencingColumns))}, 
                    ReferencedColumnUsage.TABLE_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedTable))}, 
                    ReferencedColumnUsage.COLUMN_NAME {QuoteIdentifier(nameof(ForeignKeyDefinition.ReferencedColumns))} 
                FROM 
                    INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS ReferentialConstraints 
                    INNER JOIN 
                    INFORMATION_SCHEMA.KEY_COLUMN_USAGE ReferencingColumnUsage 
                        ON ReferentialConstraints.CONSTRAINT_CATALOG = ReferencingColumnUsage.CONSTRAINT_CATALOG 
                        AND ReferentialConstraints.CONSTRAINT_SCHEMA = ReferencingColumnUsage.CONSTRAINT_SCHEMA 
                        AND ReferentialConstraints.CONSTRAINT_NAME = ReferencingColumnUsage.CONSTRAINT_NAME 
                    INNER JOIN 
                        INFORMATION_SCHEMA.KEY_COLUMN_USAGE ReferencedColumnUsage 
                        ON ReferentialConstraints.UNIQUE_CONSTRAINT_CATALOG = ReferencedColumnUsage.CONSTRAINT_CATALOG 
                        AND ReferentialConstraints.UNIQUE_CONSTRAINT_SCHEMA = ReferencedColumnUsage.CONSTRAINT_SCHEMA 
                        AND ReferentialConstraints.UNIQUE_CONSTRAINT_NAME = ReferencedColumnUsage.CONSTRAINT_NAME 
                        AND ReferencingColumnUsage.ORDINAL_POSITION = ReferencedColumnUsage.ORDINAL_POSITION 
                WHERE 
                    ReferencingColumnUsage.TABLE_SCHEMA IN (@{tableSchemaParamsForInClause})
                    AND ReferencingColumnUsage.TABLE_NAME IN (@{tableNameParamsForInClause})";

            Console.WriteLine($"Foreign Key Query: {foreignKeyQuery}");
            return foreignKeyQuery;
        }

        /// <summary>
        /// Creates a list of named parameters with incremental suffixes
        /// starting from 0 to numberOfParameters - 1.
        /// e.g. tableName0, tableName1
        /// </summary>
        /// <param name="kindOfParam">The kind of parameter being created acting
        /// as the prefix common to all parameters.</param>
        /// <param name="numberOfParameters">The number of parameters to create.</param>
        /// <returns>The created list</returns>
        public static string[] CreateParams(string kindOfParam, int numberOfParameters)
        {
            return Enumerable.Range(0, numberOfParameters).Select(i => kindOfParam + i).ToArray();
        }
    }
}
