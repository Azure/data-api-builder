using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.UriParser;

public class ODataASTVisitor<TSource> : QueryNodeVisitor<TSource>
    where TSource : class
{
    List<RestPredicate> _predicates = new();
    RestPredicate _current = new();
    public override TSource Visit(BinaryOperatorNode nodeIn)
    {
        if (nodeIn.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.And
            || nodeIn.OperatorKind == Microsoft.OData.UriParser.BinaryOperatorKind.Or)
        {
            _current.Lop = GetLogicalOperation(nodeIn.OperatorKind.ToString());
        }
        else
        {
            _current.Op = GetPredicateOperation(nodeIn.OperatorKind.ToString());
        }

        nodeIn.Right.Accept(this);
        nodeIn.Left.Accept(this);
        return null;
    }

    public override TSource Visit(SingleValuePropertyAccessNode nodeIn)
    {
        _current.Field = nodeIn.Property.Name;
        //We are finished, add current to collection, and create new RestPredicate
        _predicates.Add(_current);
        _current = new();
        return null;
    }

    public override TSource Visit(ConstantNode nodeIn)
    {
        _current.Value = nodeIn.LiteralText;
        return null;
    }

    public override TSource Visit(ConvertNode nodeIn)
    {
        nodeIn.Source.Accept(this);
        return null;
    }

    public List<RestPredicate> TryAndGetRestPredicates()
    {
        return _predicates;
    }

    public PredicateOperation GetPredicateOperation(string op)
    {
        switch (op)
        {
            case "Equal":
                return PredicateOperation.Equal;
                break;
            case "GreaterThan":
                return PredicateOperation.GreaterThan;
                break;
            case "GreaterThanOrEqual":
                return PredicateOperation.GreaterThanOrEqual;
                break;
            case "LessThan":
                return PredicateOperation.LessThan;
                break;
            case "LessThanOrEqual":
                return PredicateOperation.LessThanOrEqual;
                break;
            default:
                throw new ArgumentException($"Uknown Predicate Operation of {op}");

        }
    }
    public LogicalOperation GetLogicalOperation(string op)
    {
        switch (op)
        {
            case "And":
                return LogicalOperation.And;
                break;
            case "Or":
                return LogicalOperation.Or;
                break;
            default:
                throw new ArgumentException($"Uknown Logical Operation of {op}");

        }
    }

}
