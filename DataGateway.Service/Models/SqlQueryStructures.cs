using System;
using System.Collections.Generic;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Holds the data to describe a column
    /// </summary>
    public class Column
    {
        /// <summary>
        /// Table alias of the table which owns the column
        /// </summary>
        public string? TableAlias { get; }
        /// <summary>
        /// Name of the column
        /// </summary>
        public string ColumnName { get; }

        public Column(string? tableAlias, string columnName)
        {
            TableAlias = tableAlias;
            ColumnName = columnName;
        }
    }

    /// <summary>
    /// Extends Column with a label
    /// </summary>
    public class LabelledColumn : Column
    {
        /// <summary>
        /// This will be the column's alias
        /// </summary>
        public string Label { get; }

        public LabelledColumn(string tableAlias, string columnName, string label)
            : base(tableAlias, columnName)
        {
            Label = label;
        }
    }

    /// <summary>
    /// Represents the comparison operations a predicate can have
    /// </summary>
    public enum PredicateOperation
    {
        None,
        Equal, GreaterThan, LessThan, GreaterThanOrEqual, LessThanOrEqual, NotEqual,
        AND, OR, LIKE, NOT_LIKE
    }

    /// <summary>
    /// Operand of Predicate
    /// Can be initialized and resolved both as Column and as String
    /// </summary>
    public class PredicateOperand
    {
        /// <summary>
        /// Holds the column value when the operand is a Column
        /// </summary>
        private readonly Column? _columnOperand;
        /// <summary>
        /// Holds the string value when the operand is a string
        /// </summary>
        private readonly string? _stringOperand;
        /// <summary>
        /// Holds the predicate value when the operand is a Predicate
        /// </summary>
        private readonly Predicate? _predicateOperand;

        /// <summary>
        /// Initialize operand as Column
        /// </summary>
        public PredicateOperand(Column? column)
        {
            if (column == null)
            {
                throw new ArgumentNullException("Column predicate operand cannot be created with a null column.");
            }

            _columnOperand = column;
            _stringOperand = null;
            _predicateOperand = null;
        }

        /// <summary>
        /// Initialize operand as string
        /// </summary>
        public PredicateOperand(string? text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("String predicate operand cannot be created with a null string.");
            }

            _columnOperand = null;
            _stringOperand = text;
            _predicateOperand = null;
        }

        /// <summary>
        /// Initialize operand as Predicate
        /// </summary>
        public PredicateOperand(Predicate? predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException("A predicate operand cannot be created with a null inner predicate.");
            }

            _columnOperand = null;
            _stringOperand = null;
            _predicateOperand = predicate;
        }

        /// <summary>
        /// Resolve operand as string
        /// </summary>
        /// <returns> null if operand is not intialized as String  </returns>
        public string? AsString()
        {
            return _stringOperand;
        }

        /// <summary>
        /// Resolve operand as Column
        /// </summary>
        /// <returns> null if operand is not intialized as Column </returns>
        public Column? AsColumn()
        {
            return _columnOperand;
        }

        /// <summary>
        /// Resolve operand as Predicate
        /// </summary>
        /// <returns> null if operand is not intialized as Predicate </returns>
        public Predicate? AsPredicate()
        {
            return _predicateOperand;
        }

        /// <summary>
        /// Used to check if the predicate operand is a predicate itself
        /// </summary>
        public bool IsPredicate()
        {
            return _predicateOperand != null;
        }
    }

    /// <summary>
    /// Holds data to build
    /// {Operand1} {Operator} {Operand2}
    /// expressions
    /// </summary>
    public class Predicate
    {
        /// <summary>
        /// Left operand of the expression
        /// </summary>
        public PredicateOperand Left { get; }
        /// <summary>
        /// Right operand of the expression
        /// </summary>
        public PredicateOperand Right { get; }
        /// <summary>
        /// Enum representing the operator of the expression
        /// </summary>
        public PredicateOperation Op { get; }

        public bool AddParenthesis { get; }

        public Predicate(PredicateOperand left, PredicateOperation op, PredicateOperand right, bool addParenthesis = false)
        {
            Left = left;
            Right = right;
            Op = op;
            AddParenthesis = addParenthesis;
        }

        /// <summary>
        /// Used to check if this predicate constains nested predicates
        /// </summary>
        public bool IsNested()
        {
            return Left.IsPredicate() || Right.IsPredicate();
        }
    }

    /// <summary>
    /// Class used to store the information query builders need to
    /// build a keyset pagination predicate
    /// </summary>
    public class KeysetPaginationPredicate
    {
        /// <summary>
        /// List of primary key columns used to generate the
        /// keyset pagination predicate
        /// </summary>
        public List<Column> PrimaryKey { get; }
        /// <summary>
        /// List of values to compare the primary key with
        /// to create the pagination predicate
        /// </summary>
        public List<string> Values { get; }

        public KeysetPaginationPredicate(List<Column> primaryKey, List<string> values)
        {
            PrimaryKey = primaryKey;
            Values = values;
        }
    }

    /// <summary>
    /// IncrementingInteger provides a simple API to have an ever incrementing
    /// integer. The main usecase is so we can create aliases that are unique
    /// within a query, this integer serves as a unique part of their name.
    /// </summary>
    public class IncrementingInteger
    {
        private ulong _integer;
        public IncrementingInteger()
        {
            _integer = 0;
        }

        /// <summary>
        /// Get the next integer from this sequence of integers. The first
        /// integer that is returned is 0.
        /// </summary>
        public ulong Next()
        {
            return _integer++;
        }

    }

    /// <summary>
    /// A simple class that is used to hold the information about joins that
    /// are part of a SQL query.
    /// <summary>
    /// <param name="TableName">The name of the table that is joined with.</param>
    /// <param name="TableAlias">The alias of the table that is joined with.</param>
    /// <param name="Predicates">The predicates that are part of the ON clause of the join.</param>
    public record SqlJoinStructure(string TableName, string TableAlias, List<Predicate> Predicates);
}
