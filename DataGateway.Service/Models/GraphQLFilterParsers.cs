using System;
using System.Collections.Generic;
using HotChocolate.Language;
using System.Text;


namespace Azure.DataGateway.Service.Models {
    static public class GQLFilterParser
    {
        private const string AND = "AND";
        private const string OR = "OR";
        static public Predicate Parse(
            List<ObjectFieldNode> fields,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals,
            bool addParenthesis = false)
        {
            List<(PredicateOperation, PredicateOperand)> andOrs = new();
            List<PredicateOperand> predicates = new();

            foreach(ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();

                bool fieldIsAnd = String.Equals(name, AND, StringComparison.OrdinalIgnoreCase);
                bool fieldIsOr = String.Equals(name, OR, StringComparison.OrdinalIgnoreCase);

                if(fieldIsAnd || fieldIsOr)
                {
                    PredicateOperation op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;

                    List<IValueNode> otherPredicates = (List<IValueNode>) field.Value.Value!;
                    andOrs.Push(
                        (
                            op,
                            new PredicateOperand(ParseAndOr(otherPredicates, tableAlias, table, op, processLiterals))
                        )
                    );
                }
                else {
                    List<ObjectFieldNode> subfields = (List<ObjectFieldNode>) field.Value.Value!;
                    predicates.Push(new PredicateOperand(ParseScalarType(name, subfields, tableAlias, table, processLiterals)));
                }
            }

            if(predicates.Count == 0)
            {
                if(andOrs.Count == 1)
                {
                    return andOrs[0].Item2.AsPredicate()!;
                }
                else // andOrs.Count = 2
                {
                    return new Predicate(
                        andOrs[0].Item2,
                        andOrs[1].Item1,
                        andOrs[1].Item2
                    );
                }
            }
            else if(andOrs.Count == 0)
            {
                return MakeChainPredicate(predicates, PredicateOperation.AND);
            }
            else
            {
                if(andOrs.Count == 1)
                {
                    return new Predicate(
                        new PredicateOperand(MakeChainPredicate(predicates, PredicateOperation.AND)),
                        andOrs[0].Item1,
                        andOrs[0].Item2
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
                        ))
                    );
                }
            }
        }

        private static Predicate ParseScalarType(
            string tag,
            List<ObjectFieldNode> fields,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals)
        {
            Column column = new Column(tableAlias, tag);
            Type columnType = ColumnDefinition.ResolveColumnTypeToSystemType(table.Columns[tag].Type);
            switch(columnType.ToString())
            {
                case "System.String":
                    return StringTypeFilterParser.Parse(column, fields, processLiterals);
                case "System.Int64":
                    return IntTypeFilterParser.Parse(column, fields, processLiterals);
                default:
                    throw new NotSupportedException($"Unexpected system type {columnType} found for column.");
            }
        }

        private static Predicate ParseAndOr(
            List<IValueNode> otherPredicates,
            string tableAlias,
            TableDefinition table,
            PredicateOperation op,
            Func<object, string> processLiterals)
        {
            if(otherPredicates.Count == 0)
            {
                return new Predicate(
                    new PredicateOperand("1"),
                    op == PredicateOperation.AND ? PredicateOperation.Equal : PredicateOperation.NotEqual,
                    new PredicateOperand("1")
                );
            }

            List<PredicateOperand> operands = new();
            foreach(IValueNode predicate in otherPredicates)
            {
                List<ObjectFieldNode> fields = (List<ObjectFieldNode>) predicate.Value!;
                operands.Add(new PredicateOperand(Parse(fields, tableAlias, table, processLiterals)));
            }

            return MakeChainPredicate(operands, op, addParenthesis: true);
        }

        public static Predicate MakeChainPredicate(
            List<PredicateOperand> operands,
            PredicateOperation op,
            int pos = 0,
            bool addParenthesis = false)
        {
            if(pos == operands.Count - 1)
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

    static public class IntTypeFilterParser {
        static public Predicate Parse(
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            foreach(ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();
                int value = ((IntValueNode) fields[0].Value).ToInt32();

                PredicateOperation op;
                switch(name)
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

            return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND, addParenthesis: true);
        }
    }

    static public class StringTypeFilterParser {
        static public Predicate Parse(
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            List<PredicateOperand> predicates = new();

            foreach(ObjectFieldNode field in fields)
            {
                string ruleName = field.Name.ToString();
                string ruleValue = ((StringValueNode)field.Value).Value;

                PredicateOperation op;

                switch(ruleName)
                {
                    case "eq":
                        op = PredicateOperation.Equal;
                        break;
                    case "neq":
                        op = PredicateOperation.NotEqual;
                        break;
                    case "contains":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"%{ruleValue}%";
                        break;
                    case "notContains":
                        op = PredicateOperation.NOT_LIKE;
                        ruleValue = $"%{ruleValue}%";
                        break;
                    case "startsWith":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"{ruleValue}%";
                        break;
                    case "endsWith":
                        op = PredicateOperation.LIKE;
                        ruleValue = $"%{ruleValue}";
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

            return GQLFilterParser.MakeChainPredicate(predicates, PredicateOperation.AND, addParenthesis: true);
        }
    }
}
