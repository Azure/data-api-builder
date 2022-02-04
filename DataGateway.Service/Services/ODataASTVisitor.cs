using System;
using System.Collections.Generic;
using Azure.DataGateway.Service.Models;
using Microsoft.OData.UriParser;

public class ODataASTVisitor<TSource> : QueryNodeVisitor<TSource>
    where TSource : class
{
    Dictionary<string, Tuple<object, PredicateOperation>> _predicates = new();
    object _value;
    PredicateOperation _op;
    public override TSource Visit(BinaryOperatorNode nodeIn)
    {

        _op = GetPredicateOperation(nodeIn.OperatorKind.ToString());
        nodeIn.Right.Accept(this);
        nodeIn.Left.Accept(this);
        return null;
    }

    public override TSource Visit(SingleValuePropertyAccessNode nodeIn)
    {
        string field = nodeIn.Property.Name;
        Tuple<object, PredicateOperation> valueAndOp = new(_value, _op);
        //We are finished, add current to collection.
        _predicates.Add(field, valueAndOp);
        return null;
    }

    public override TSource Visit(ConstantNode nodeIn)
    {
        _value = nodeIn.LiteralText;
        return null;
    }

    public Dictionary<string, Tuple<object, PredicateOperation>> TryAndGetRestPredicates()
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
            case "And":
                return PredicateOperation.And;
                break;
            case "Or":
                return PredicateOperation.Or;
                break;
            default:
                throw new ArgumentException($"Uknown Predicate Operation of {op}");

        }
    }

}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Services
{
    public class ODataASTVisitor
    {
    }
}
