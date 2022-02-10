using System;
using System.Text;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.OData.UriParser;

/// <summary>
/// This class is a visitor for an AST generated when parsing a $filter query string
/// with the OData Uri Parser.
/// </summary>
public class ODataASTVisitor<TSource> : QueryNodeVisitor<TSource>
    where TSource : class
{
    StringBuilder _filterPredicateBuilder = new();
    SqlQueryStructure _struct;
    string _field;
    string _value;
    string _op;

    public ODataASTVisitor(SqlQueryStructure structure)
    {
        _struct = structure;
    }

    /// <summary>
    /// Represents visiting a BinaryOperatorNode, which will hold either
    /// a Predicate operation (eq, gt, lt, etc), or a Logical operaton (And, Or).
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override TSource Visit(BinaryOperatorNode nodeIn)
    {
        // In order traversal but add parens to maintain order of logical operations
        if (IsLogicalNode(nodeIn))
        {
            _filterPredicateBuilder.Append("(");
            nodeIn.Left.Accept(this);
            _filterPredicateBuilder.Append($" {GetFilterPredicateOperator(nodeIn.OperatorKind)} ");
            nodeIn.Right.Accept(this);
            _filterPredicateBuilder.Append(")");
        }
        else
        {
            nodeIn.Left.Accept(this);
            _op = GetFilterPredicateOperator(nodeIn.OperatorKind);
            nodeIn.Right.Accept(this);
            // At this point we have everything we need to paramaterize and save the predicate
            string paramName = _struct.MakeParamWithValue(_struct.GetParamAsColumnSystemType(_value, _field));
            _filterPredicateBuilder.Append($"{_field} {_op} @{paramName}");
        }

        return null;
    }

    /// <summary>
    /// Represents visint a UnaryNode, which is what holds unary
    /// operators such as NOT.
    /// </summary>
    /// <param name="nodeIn">The node visisted.</param>
    /// <returns></returns>
    public override TSource Visit(UnaryOperatorNode nodeIn)
    {
        _filterPredicateBuilder.Append("(");
        _filterPredicateBuilder.Append($"{GetFilterPredicateOperator(nodeIn.OperatorKind)} ");
        nodeIn.Operand.Accept(this);
        _filterPredicateBuilder.Append(")");
        return null;
    }

    /// <summary>
    /// Represents visiting a SingleValuePropertyAccessNode, which is what
    /// holds a field name in the AST.
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override TSource Visit(SingleValuePropertyAccessNode nodeIn)
    {
        // save field to paramaterize later
        _field = nodeIn.Property.Name;
        return null;
    }

    /// <summary>
    /// Represents visiting a ConstantNode, which is what
    /// holds a value in the AST. 
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override TSource Visit(ConstantNode nodeIn)
    {
        _value = nodeIn.Value.ToString();
        return null;
    }

    /// <summary>
    /// Represents visiting a ConvertNode, which holds
    /// some other node as its source.
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override TSource Visit(ConvertNode nodeIn)
    {
        // call accept on source to keep traversing AST.
        nodeIn.Source.Accept(this);
        return null;
    }

    /// <summary>
    /// Returns the string representation of the filter predicates
    /// that will make up the filter of the query.
    /// </summary>
    /// <returns>String representing filter predicates.</returns>
    public string TryAndGetFindPredicates()
    {
        return _filterPredicateBuilder.ToString();
    }

    /// <summary>
    /// Return the correct string for the binary operator that will be a part of the filter predicates
    /// that will make up the filter of the query.
    /// </summary>
    /// <param name="op">The op we will translate.</param>
    /// <returns>The string which is a translation of the op.</returns>
    public string GetFilterPredicateOperator(BinaryOperatorKind op)
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
                throw new ArgumentException($"Uknown Predicate Operation of {op}");
        }
    }

    /// <summary>
    /// Return the correct string for the unary operator that will be a part of the filter predicates
    /// that will make up the filter of the query.
    /// </summary>
    /// <param name="op">The op we will translate.</param>
    /// <returns>The string which is a translation of the op.</returns>
    public string GetFilterPredicateOperator(UnaryOperatorKind op)
    {
        switch (op)
        {
            case UnaryOperatorKind.Not:
                return "NOT";
            default:
                throw new ArgumentException($"Uknown Predicate Operation of {op}");
        }
    }

    /// <summary>
    /// Checks if the kind of the provided node is consistent with a logical operation.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns>Bool representing if the node is a kind of logical operation.</returns>
    public bool IsLogicalNode(SingleValueNode node)
    {
        // Node is a BinaryOperatorNode so we just check the OperatorKind
        if (node is BinaryOperatorNode)
        {
            BinaryOperatorNode bNode = (BinaryOperatorNode)node;
            return bNode.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.And
            || bNode.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.Or;
        }
        // Node is a convert node so we must check source instead
        else if (node is ConvertNode)
        {
            ConvertNode cNode = (ConvertNode)node;
            return IsLogicalNode(cNode.Source);
        }

        return false;
    }
}
