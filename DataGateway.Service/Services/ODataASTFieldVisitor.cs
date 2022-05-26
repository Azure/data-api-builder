using System.Collections.Generic;
using Microsoft.OData.UriParser;

namespace Azure.DataGateway.Service.Services
{
    /// <summary>
    /// OData Visitor implementation which traverses the abstract syntax tree (AST)
    /// of a request's filter clause(s).
    /// This visitor enumerates a unique list of columns present within a filter clause,
    /// so that the authorization engine can check for the presence of unauthorized columns.
    /// </summary>
    public class ODataASTFieldVisitor : QueryNodeVisitor<string>
    {
        /// <summary>
        /// A collection of all unique columns names present in the Abstract Syntax Tree (AST).
        /// </summary>
        public HashSet<string> CumulativeColumns { get; } = new();

        /// <summary>
        /// Represents visiting a BinaryOperatorNode, which will hold either
        /// a Predicate operation (eq, gt, lt, etc), or a Logical operaton (And, Or).
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String concatenation of (left op right).</returns>
        public override string Visit(BinaryOperatorNode nodeIn)
        {
            // In order traversal but add parens to maintain order of logical operations
            nodeIn.Left.Accept(this);
            nodeIn.Right.Accept(this);
            return string.Empty;
        }

        /// <summary>
        /// Represents visiting a UnaryNode, which is what holds unary
        /// operators such as NOT.
        /// </summary>
        /// <param name="nodeIn">The node visisted.</param>
        /// <returns>String concatenation of (op children)</returns>
        public override string Visit(UnaryOperatorNode nodeIn)
        {
            nodeIn.Operand.Accept(this);
            return string.Empty;
        }

        /// <summary>
        /// Represents visiting a SingleValuePropertyAccessNode, which is what
        /// holds a field name in the AST.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String representing the Field name</returns>
        public override string Visit(SingleValuePropertyAccessNode nodeIn)
        {
            if (nodeIn is not null)
            {
                CumulativeColumns.Add(nodeIn.Property.Name.ToString());
            }

            return string.Empty;
        }

        /// <summary>
        /// Represents visiting a ConstantNode, which is what
        /// holds a value in the AST. 
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns>String representing param that holds given value.</returns>
        public override string Visit(ConstantNode nodeIn)
        {
            return string.Empty;
        }

        /// <summary>
        /// Represents visiting a ConvertNode, which holds
        /// some other node as its source.
        /// </summary>
        /// <param name="nodeIn">The node visited.</param>
        /// <returns></returns>
        public override string Visit(ConvertNode nodeIn)
        {
            // call accept on source to keep traversing AST.
            return nodeIn.Source.Accept(this);
        }
    }
}
