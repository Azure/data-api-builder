// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using HotChocolate.Language;
using HotChocolate.Types;

namespace Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
public class TimeOnlyType : ScalarType<TimeOnly>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimeOnlyType"/> class.
    /// </summary>
    public TimeOnlyType() : base("TimeOnly")
    { }

    /// <inheritdoc/>
    public override bool IsInstanceOfType(IValueNode valueSyntax)
    {
        return valueSyntax is StringValueNode;
    }

    /// <inheritdoc/>
    public override object ParseLiteral(IValueNode literal)
    {
        if (literal is StringValueNode stringValueNode)
        {
            if (TimeSpan.TryParse(stringValueNode.Value, out TimeSpan timeSpan))
            {
                return new TimeOnly(timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
            }
        }

        throw new ArgumentException("Invalid time value.");
    }

    /// <inheritdoc/>
    public override IValueNode ParseResult(object? resultValue)
    {
        if (resultValue is TimeOnly timeOnly)
        {
            return new StringValueNode(timeOnly.ToString(@"HH\:mm\:ss\.fff"));
        }

        throw new ArgumentException("Invalid TimeOnly value.");
    }

    /// <inheritdoc/>
    public override IValueNode ParseValue(object? value)
    {
        if (value is TimeOnly timeOnly)
        {
            return new StringValueNode(timeOnly.ToString(@"HH\:mm\:ss\.fff"));
        }

        throw new ArgumentException("Invalid TimeOnly value.");
    }

    /// <inheritdoc/>
    public override object Serialize(object? value)
    {
        if (value is TimeOnly timeOnly)
        {
            return timeOnly.ToString(@"HH\:mm\:ss\.fff");
        }

        throw new ArgumentException("Invalid TimeOnly value.");
    }
}
