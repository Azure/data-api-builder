using System;
using System.Collections.Generic;
using HotChocolate.Language;
using System.Linq;


namespace Azure.DataGateway.Service.Models {
    static public class GQLFilterParser
    {
        private const string AND = "AND";
        private const string OR = "OR";
        private const string IN_PRT = "inPrt";
        static public Predicate Parse(
            string tag,
            List<ObjectFieldNode> fields,
            string tableAlias,
            TableDefinition table,
            Func<object, string> processLiterals,
            bool addParenthesis = false)
        {
            bool isAnd = false;
            bool isOr = false;
            PredicateOperand? left = null;
            PredicateOperation op = PredicateOperation.None;
            PredicateOperand? right = null;

            foreach(ObjectFieldNode field in fields)
            {
                string name = field.Name.ToString();

                bool fieldIsAnd = String.Equals(name, AND, StringComparison.OrdinalIgnoreCase);
                bool fieldIsOr = String.Equals(name, OR, StringComparison.OrdinalIgnoreCase);

                isAnd = isAnd || fieldIsAnd;
                isOr = isOr || fieldIsOr;

                if(fieldIsAnd || fieldIsOr)
                {
                    if(right != null)
                    {
                        throw new FormatException($"{tag} filter cannot have both \"and\" and \"or\"");
                    }

                    op = fieldIsAnd ? PredicateOperation.AND : PredicateOperation.OR;

                    List<IValueNode> otherPredicates = (List<IValueNode>) field.Value.Value!;
                    right = new PredicateOperand(ParseAndOr(name, otherPredicates, tableAlias, table, op, processLiterals));
                }
                else {
                    if(left != null)
                    {
                        throw new FormatException($"{tag} has too many predicates.");
                    }

                    List<ObjectFieldNode> subfields = (List<ObjectFieldNode>) field.Value.Value!;

                    left = String.Equals(name, IN_PRT, StringComparison.OrdinalIgnoreCase) ?
                        new PredicateOperand(Parse(name, subfields, tableAlias, table, processLiterals, addParenthesis: true))
                        : new PredicateOperand(ParseScalarType(name, subfields, tableAlias, table, processLiterals));

                }
            }

            if(left == null)
            {
                throw new FormatException($"{tag} doesn't have any predicates.");
            }

            if(right == null)
            {
                return left.AsPredicate()!;
            }
            else {
                return new Predicate(left, op, right, addParenthesis);
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
                    return StringTypeFilterParser.Parse(tag, column, fields, processLiterals);
                case "System.Int64":
                    return IntTypeFilterParser.Parse(tag, column, fields, processLiterals);
                default:
                    throw new NotSupportedException($"Unexpected system type {columnType} found for column.");
            }
        }

        private static Predicate ParseAndOr(
            string tag,
            List<IValueNode> otherPredicates,
            string tableAlias,
            TableDefinition table,
            PredicateOperation op,
            Func<object, string> processLiterals)
        {
            if(otherPredicates.Count == 0)
            {
                throw new FormatException($"{tag} cannot be an empty list.");
            }

            List<PredicateOperand> operands = new();
            foreach(IValueNode predicate in otherPredicates)
            {
                List<ObjectFieldNode> fields = (List<ObjectFieldNode>) predicate.Value!;
                operands.Add(new PredicateOperand(Parse($"{tag}[{operands.Count}]", fields, tableAlias, table, processLiterals)));
            }

            return MakeChainPredicate(operands, op);
        }

        private static Predicate MakeChainPredicate(List<PredicateOperand> operands, PredicateOperation op, int pos = 0)
        {
            if(pos == operands.Count - 1)
            {
                return operands[pos].AsPredicate()!;
            }

            return new Predicate(
                operands[pos],
                op,
                new PredicateOperand(MakeChainPredicate(operands, op, pos + 1))
            );
        }
    }

    static public class IntTypeFilterParser {
        static public Predicate Parse(
            string tag, Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            if(fields.Count > 1)
            {
                throw new FormatException($"Cannot have more than one rule in integer predicate in {tag}");
            }

            string name = fields[0].Name.ToString();
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

            return new Predicate(
                        new PredicateOperand(column),
                        op,
                        new PredicateOperand($"@{processLiterals(value)}"));
        }
    }

    static public class StringTypeFilterParser {
        static public Predicate Parse(
            string tag,
            Column column,
            List<ObjectFieldNode> fields,
            Func<object, string> processLiterals)
        {
            if(fields.Count > 1)
            {
                throw new FormatException($"Cannot have more than one rule in integer predicate in {tag}");
            }

            string name = fields[0].Name.ToString();
            string value = fields[0].Value.ToString();

            PredicateOperation op;

            throw new NotImplementedException();
        }
    }
}
