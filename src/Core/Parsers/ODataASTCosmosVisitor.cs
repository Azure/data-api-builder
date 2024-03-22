// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.UriParser;

/// <summary>
/// This class is a visitor for an AST generated when parsing a $filter query string
/// with the OData Uri Parser for Cosmos DB Db Policies
/// </summary>
namespace Azure.DataApiBuilder.Core.Parsers
{
    internal class ODataASTCosmosVisitor : QueryNodeVisitor<string>
    {
        private string _prefix;

        /// <summary>
        /// Constructor for the visitor to append prefix to the column names which would be the path from container to the column
        /// </summary>
        /// <param name="prefix"></param>
        public ODataASTCosmosVisitor(string prefix)
        {
            this._prefix = prefix;
        }

        /// <summary>
        /// Represents visiting a SingleValuePropertyAccessNode, which is what
        /// holds an exposed field name in the AST.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String representing the Field name</returns>
        public override string Visit(SingleValuePropertyAccessNode nodeIn)
        {
            return ($"{_prefix}.{nodeIn.Property.Name}");
        }

        /// <summary>
        /// Represents visiting a BinaryOperatorNode, which will hold either
        /// a Predicate operation (eq, gt, lt, etc), or a Logical operation (And, Or).
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (left op right).</returns>
        public override string Visit(BinaryOperatorNode nodeIn)
        {
            string left = nodeIn.Left.Accept(this);
            string right = nodeIn.Right.Accept(this);

            if (left.Equals("NULL", StringComparison.OrdinalIgnoreCase) || right.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                return CreateNullResult(nodeIn.OperatorKind, left, right);
            }

            return $"({left} {GetFilterPredicateOperator(nodeIn.OperatorKind)} {right})";
        }

        /// <summary>
        /// Create the correctly formed response with NULLs.
        /// </summary>
        /// <param name="op">The binary operation</param>
        /// <param name="field">The value representing a field.</param>
        /// <returns>The correct format for a NULL given the op and left hand side.</returns>
        private static string CreateNullResult(BinaryOperatorKind op, string left, string right)
        {
            switch (op)
            {
                case BinaryOperatorKind.Equal:
                    if (!left.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"({left} IS NULL)";
                    }

                    return $"({right} IS NULL)";
                case BinaryOperatorKind.NotEqual:
                    if (!left.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"({left} IS NOT NULL)";
                    }

                    return $"({right} IS NOT NULL)";
                case BinaryOperatorKind.GreaterThan:
                case BinaryOperatorKind.GreaterThanOrEqual:
                case BinaryOperatorKind.LessThan:
                case BinaryOperatorKind.LessThanOrEqual:
                    return $"({left} {GetFilterPredicateOperator(op)} {right})";
                default:
                    throw new NotSupportedException($"{op} is not supported with {left} and {right}");
            }
        }

        /// <summary>
        /// Represents visiting a UnaryNode, which is what holds unary
        /// operators such as NOT.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (op children)</returns>
        public override string Visit(UnaryOperatorNode nodeIn)
        {
            string child = nodeIn.Operand.Accept(this);
            return $"({GetFilterPredicateOperator(nodeIn.OperatorKind)} {child} )";
        }

        /// <summary>
        /// Represents visiting a ConvertNode, which holds
        /// some other node as its source.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns></returns>
        public override string Visit(ConvertNode nodeIn)
        {
            return nodeIn.Source.Accept(this);
        }

        /// <summary>
        /// Represents visiting a ConstantNode, which is what
        /// holds a value in the AST.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String representing param that holds given value.</returns>
        public override string Visit(ConstantNode nodeIn)
        {
            if (nodeIn.TypeReference is null)
            {
                // Represents a NULL value, we support NULL in queries so return "NULL" here
                return "NULL";
            }

            return $"'{nodeIn.Value}'";
        }

        /// <summary>
        /// Return the correct string for the binary operator that will be a part of the filter predicates
        /// that will make up the filter of the query.
        /// </summary>
        /// <param name="op">The op we will translate.</param>
        /// <returns>The string which is a translation of the op.</returns>
        private static string GetFilterPredicateOperator(BinaryOperatorKind op)
        {
            switch (op)
            {
                case BinaryOperatorKind.Equal:
                    return "=";
                case BinaryOperatorKind.GreaterThan:
                    return ">";
                case BinaryOperatorKind.GreaterThanOrEqual:
                    return ">=";
                case BinaryOperatorKind.LessThan:
                    return "<";
                case BinaryOperatorKind.LessThanOrEqual:
                    return "<=";
                case BinaryOperatorKind.NotEqual:
                    return "!=";
                case BinaryOperatorKind.And:
                    return "AND";
                case BinaryOperatorKind.Or:
                    return "OR";
                default:
                    throw new ArgumentException($"Unknown Predicate Operation of {op}");
            }
        }

        /// <summary>
        /// Return the correct string for the unary operator that will be a part of the filter predicates
        /// that will make up the filter of the query.
        /// </summary>
        /// <param name="op">The op we will translate.</param>
        /// <returns>The string which is a translation of the op.</returns>
        private static string GetFilterPredicateOperator(UnaryOperatorKind op)
        {
            switch (op)
            {
                case UnaryOperatorKind.Not:
                    return "NOT";
                default:
                    throw new ArgumentException($"Unknown Predicate Operation of {op}");
            }
        }
    }
}
