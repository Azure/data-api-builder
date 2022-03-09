using System;
using System.Collections.Generic;
using HotChocolate.Language;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Contains methods to parse a GQL _filter parameter
    /// </summary>
    static public class GQLFilterParser
    {
        private const string AND = "AND";
        private const string OR = "OR";

        /// <summary>
        /// Parse a predicate for a *FilterInput input type
        /// </summary>
        /// <param name="fields">The fields in the *FilterInput being processed</param>
        /// <param name="tableAlias">The table alias underlyin the *FilterInput being processed</param>
        /// <param name="table">The table underlyin the *FilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        static public Predicate Parse(
            List<ObjectFieldNode> fields,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals)
        {
            List<(PredicateOperation, PredicateOperand)> andOrs = new();
            List<PredicateOperand> predicates = new();

            foreach (ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();

                bool fieldIsAnd = String.Equals(name, AND, StringComparison.OrdinalIgnoreCase);
                bool fieldIsOr = String.Equals(name, OR, StringComparison.OrdinalIgnoreCase);

                if (fieldIsAnd || fieldIsOr)
                {
                    PredicateOperation op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;

                    List<IValueNode> otherPredicates = (List<IValueNode>)field.Value.Value!;
                    andOrs.Push(
                        (
                            op,
                            new PredicateOperand(ParseAndOr(otherPredicates, tableAlias, table, op, processLiterals))
                        )
                    );
                }
                else
                {
                    List<ObjectFieldNode> subfields = (List<ObjectFieldNode>)field.Value.Value!;
                    predicates.Push(new PredicateOperand(ParseScalarType(name, subfields, tableAlias, table, processLiterals)));
                }
            }

            if (predicates.Count == 0)
            {
                if (andOrs.Count == 1)
                {
                    return andOrs[0].Item2.AsPredicate()!;
                }
                else // andOrs.Count = 2
                {
                    return new Predicate(
                        andOrs[0].Item2,
                        andOrs[1].Item1,
                        andOrs[1].Item2,
                        addParenthesis: true
                    );
                }
            }
            else if (andOrs.Count == 0)
            {
                return MakeChainPredicate(predicates, PredicateOperation.AND);
            }
            else
            {
                if (andOrs.Count == 1)
                {
                    return new Predicate(
                        new PredicateOperand(MakeChainPredicate(predicates, PredicateOperation.AND)),
                        andOrs[0].Item1,
                        andOrs[0].Item2,
                        addParenthesis: true
                    );
                }
                else // andOrs.Count = 2
                {
                    return new Predicate(
                        new PredicateOperand(MakeChainPredicate(predicates, PredicateOperation.AND)),
                        andOrs[0].Item1,
                        new PredicateOperand(new Predicate(
                            andOrs[0].Item2,
                            andOrs[1].Item1,
                            andOrs[1].Item2
                        )),
                        addParenthesis: true
                    );
                }
            }
        }

        /// <summary>
        /// Calls the appropriate scalar type filter parser based on the type of
        /// the underlying table column
        /// </summary>
        private static Predicate ParseScalarType(
            string name,
            List<ObjectFieldNode> fields,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals)
        {
            Column column = new(tableAlias, name);
            Type columnType = ColumnDefinition.ResolveColumnTypeToSystemType(table.Columns[name].Type);
            switch (columnType.ToString())
            {
                case "System.String":
                    return StringTypeFilterParser.Parse(column, fields, processLiterals);
                case "System.Int64":
                    return IntTypeFilterParser.Parse(column, fields, processLiterals);
                default:
                    throw new NotSupportedException($"Unexpected system type {columnType} found for column.");
            }
        }

        /// <summary>
        /// Parse the list of *FilterInput objects passed in and/or fields into a single predicate
        /// </summary>
        /// <returns>
        /// The predicate representation of the and/or.
        /// If and/or is passed as empty, a predicate representing 1 != 1 is returned
        /// </returns>
        private static Predicate ParseAndOr(
            List<IValueNode> predicates,
            string tableAlias,
            TableDefinition table,
            PredicateOperation op,
            Func<object, string> processLiterals)
        {
            if (predicates.Count == 0)
            {
                return new Predicate(
                    new PredicateOperand("1"),
                    PredicateOperation.NotEqual,
                    new PredicateOperand("1")
                );
            }

            List<PredicateOperand> operands = new();
            foreach (IValueNode predicate in predicates)
            {
                List<ObjectFieldNode> fields = (List<ObjectFieldNode>)predicate.Value!;
                operands.Add(new PredicateOperand(Parse(fields, tableAlias, table, processLiterals)));
            }

            return MakeChainPredicate(operands, op);
        }

        /// <summary>
        /// Merge a list of predicate operands into a single predicate
        /// </summary>
        public static Predicate MakeChainPredicate(
            List<PredicateOperand> operands,
            PredicateOperation op,
            int pos = 0,
            bool addParenthesis = true)
        {
            if (pos == operands.Count - 1)
            {
                return operands[pos].AsPredicate()!;
            }

            return new Predicate(
                operands[pos],
                op,
                new PredicateOperand(MakeChainPredicate(operands, op, pos + 1, false)),
                addParenthesis: addParenthesis && operands.Count > 1
            );
        }
    }

    /// <summary>
    /// Contains methods to parse a IntFilterInput
    /// </summary>
    static public class IntTypeFilterParser
    {

        /// <summary>
        /// Parse a predicate for a IntFilterInput input type
        /// </summary>
        /// <param name="column">A Column representing the table column being filtered by the IntFilterInput</param>
        /// <param name="fields">The fields in the IntFilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        static public Predicate Parse(
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            foreach (ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();
                int value = ((IntValueNode)field.Value).ToInt32();

                PredicateOperation op;
                switch (name)
                {
                    case "eq":
                        op = PredicateOperation.Equal;
                        break;
                    case "neq":
                        op = PredicateOperation.NotEqual;
                        break;
                    case "lt":
                        op = PredicateOperation.LessThan;
                        break;
                    case "gt":
                        op = PredicateOperation.GreaterThan;
                        break;
                    case "lte":
                        op = PredicateOperation.LessThanOrEqual;
                        break;
                    case "gte":
                        op = PredicateOperation.GreaterThanOrEqual;
                        break;
                    default:
                        throw new NotSupportedException($"Operation {name} on int type not supported.");
                }

                predicates.Push(new PredicateOperand(new Predicate(
                    new PredicateOperand(column),
                    op,
                    new PredicateOperand($"@{processLiterals(value)}")
                )));
            }

            return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND);
        }
    }

    /// <summary>
    /// Contains methods to parse a StringFilterInput
    /// </summary>
    static public class StringTypeFilterParser
    {

        /// <summary>
        /// Parse a predicate for a StringFilterInput input type
        /// </summary>
        /// <param name="column">A Column representing the table column being filtered by the StringFilterInput</param>
        /// <param name="fields">The fields in the StringFilterInput being processed</param>
        /// <param name="processLiterals">Parametrizes literals before they are written in string predicate operands</param>
        static public Predicate Parse(
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            foreach (ObjectFieldNode field in fields)
            {
                string ruleName = field.Name.ToString();
                string ruleValue = ((StringValueNode)field.Value).Value;

                PredicateOperation op;

                switch (ruleName)
                {
                    case "eq":
                        op = PredicateOperation.Equal;
                        break;
                    case "neq":
                        op = PredicateOperation.NotEqual;
                        break;
                    case "contains":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"%{EscapeLikeString(ruleValue)}%";
                        break;
                    case "notContains":
                        op = PredicateOperation.NOT_LIKE;
                        ruleValue = $"%{EscapeLikeString(ruleValue)}%";
                        break;
                    case "startsWith":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"{EscapeLikeString(ruleValue)}%";
                        break;
                    case "endsWith":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"%{EscapeLikeString(ruleValue)}";
                        break;
                    default:
                        throw new NotSupportedException($"Operation {ruleName} on int type not supported.");
                }

                predicates.Push(new PredicateOperand(new Predicate(
                    new PredicateOperand(column),
                    op,
                    new PredicateOperand($"@{processLiterals(ruleValue)}")
                )));
            }

            return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND);
        }

        /// <summary>
        /// Escape character special to the LIKE operator in sql
        /// </summary>
        private static string EscapeLikeString(string input)
        {
            input = input.Replace(@"\", @"\\");
            input = input.Replace(@"%", @"\%");
            input = input.Replace(@"[", @"\[");
            input = input.Replace(@"]", @"\]");
            input = input.Replace(@"_", @"\_");
            return input;
        }
    }
}
