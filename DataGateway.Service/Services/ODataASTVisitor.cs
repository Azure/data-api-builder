using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.UriParser;

/// <summary>
/// This class is a visitor for an AST generated when parsing a $filter query string
/// with the OData Uri Parser.
/// </summary>
public class ODataASTVisitor<TSource> : QueryNodeVisitor<TSource>
    where TSource : class
{
    List<RestPredicate> _predicates = new();
    RestPredicate _current = new();

    /// <summary>
    /// Represents visiting a BinaryOperatorNode, which will hold either
    /// a Predicate operation (eq, gt, lt, etc), or a Logical operaton (And, Or).
    /// </summary>
    /// <param name="nodeIn">The node visited.</param>
    /// <returns></returns>
    public override TSource Visit(BinaryOperatorNode nodeIn)
    {
        // If the node we currently visit is a logical operator we need to check its children
        if (nodeIn.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.And
            || nodeIn.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.Or)
        {
            // first save logical op
            _current.Lop = GetLogicalOperation(nodeIn.OperatorKind.ToString());

            // child could be another logical op, in which case we recursively call on the other child first
            if (IsLogicalNode(nodeIn.Left))
            {
                nodeIn.Right.Accept(this);
                nodeIn.Left.Accept(this);
                return null;
            }
            else if (IsLogicalNode(nodeIn.Right))
            {
                nodeIn.Left.Accept(this);
                nodeIn.Right.Accept(this);
                return null;
            }

            // Parent is logical operator both children are not, so we save parent's logical op for left child
            LogicalOperation tempOp = _current.Lop;
            // right child already has parent's logical op
            nodeIn.Right.Accept(this);
            _current.Lop = tempOp;
            nodeIn.Left.Accept(this);
            return null;
        }
        else
        {
            // not a logical op so save the predicate op
            _current.Op = GetPredicateOperation(nodeIn.OperatorKind.ToString());
        }

        // not a logical op so just call accept on children
        nodeIn.Right.Accept(this);
        nodeIn.Left.Accept(this);
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
        _current.Field = nodeIn.Property.Name;
        // we call right then left, so field name comes when we are done, add to list and create new _current
        _predicates.Add(_current);
        _current = new();
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
        _current.Value = nodeIn.Value.ToString();
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
    /// Gets and returns _predicates
    /// </summary>
    /// <returns>List of Rest Predicates.</returns>
    public List<RestPredicate> TryAndGetRestPredicates()
    {
        return _predicates;
    }

    /// <summary>
    /// Helper function to return the predicate operation associated with a given string.
    /// </summary>
    /// <param name="op">The string representing the predicate operation to return.</param>
    /// <returns>Predicate operation that represents the string provided.</returns>
    public PredicateOperation GetPredicateOperation(string op)
    {
        switch (op)
        {
            case "Equal":
                return PredicateOperation.Equal;
            case "GreaterThan":
                return PredicateOperation.GreaterThan;
            case "GreaterThanOrEqual":
                return PredicateOperation.GreaterThanOrEqual;
            case "LessThan":
                return PredicateOperation.LessThan;
            case "LessThanOrEqual":
                return PredicateOperation.LessThanOrEqual;
            case "NotEqual":
                return PredicateOperation.NotEqual;
            default:
                throw new ArgumentException($"Uknown Predicate Operation of {op}");
        }
    }

    /// <summary>
    /// Helper function to return the logical operation associated with a given string.
    /// </summary>
    /// <param name="op">The string representing the logical operation to return.</param>
    /// <returns>Logical operation that represents the string provided.</returns>
    public LogicalOperation GetLogicalOperation(string op)
    {
        switch (op)
        {
            case "And":
                return LogicalOperation.And;
            case "Or":
                return LogicalOperation.Or;
            default:
                throw new ArgumentException($"Uknown Logical Operation of {op}");
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
