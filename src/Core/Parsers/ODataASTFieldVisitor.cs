// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// OData Visitor implementation which traverses the abstract syntax tree (AST)
    /// of a request's filter clause(s).
    /// This visitor enumerates a unique list of columns present within a filter clause,
    /// so that the authorization engine can check for the presence of unauthorized columns.
    /// The visitor does not create a result to return and instead stores the CumulativeColumn
    /// set to be referenced by authorization code.
    /// </summary>
    public class ODataASTFieldVisitor : QueryNodeVisitor<object?>
    {
        /// <summary>
        /// A collection of all unique column names present in the Abstract Syntax Tree (AST).
        /// </summary>
        public HashSet<string> CumulativeColumns { get; } = new();

        /// <summary>
        /// Represents visiting a BinaryOperatorNode, which will hold either
        /// a Predicate operation (eq, gt, lt, etc), or a Logical operaton (And, Or).
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (left op right).</returns>
        public override object? Visit(BinaryOperatorNode nodeIn)
        {
            // In order traversal but add parens to maintain order of logical operations
            nodeIn.Left.Accept(this);
            nodeIn.Right.Accept(this);
            return null;
        }

        /// <summary>
        /// Represents visiting a UnaryNode, which is what holds unary
        /// operators such as NOT.
        /// </summary>
        /// <param name="nodeIn">The node visisted.</param>
        public override object? Visit(UnaryOperatorNode nodeIn)
        {
            nodeIn.Operand.Accept(this);
            return null;
        }

        /// <summary>
        /// Represents visiting a SingleValuePropertyAccessNode, which is what
        /// holds a field name in the AST.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        public override object? Visit(SingleValuePropertyAccessNode nodeIn)
        {
            CumulativeColumns.Add(nodeIn.Property.Name);
            return null;
        }

        /// <summary>
        /// Represents visiting a ConstantNode, which is what
        /// holds a value in the AST. 
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        public override object? Visit(ConstantNode nodeIn)
        {
            return null;
        }

        /// <summary>
        /// Represents visiting a ConvertNode, which holds
        /// some other node as its source.
        /// Calls accept on source to keep traversing AST.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns></returns>
        public override object? Visit(ConvertNode nodeIn)
        {
            return nodeIn.Source.Accept(this);
        }
    }
}
