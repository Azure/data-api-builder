// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.OData.UriParser;

namespace Azure.DataApiBuilder.Core.Parsers
{
    /// <summary>
    /// Shared handling for the OData string functions supported in $filter and database policies:
    /// contains, startswith and endswith. The first argument must be a field and the second must
    /// resolve to a non-null string, either a constant (including a bound claim value) or a
    /// concat of such values. Folding concat here keeps the whole search value in one parameter.
    /// </summary>
    internal static class ODataStringFunctionHelper
    {
        public const string CONTAINS = "contains";
        public const string STARTSWITH = "startswith";
        public const string ENDSWITH = "endswith";
        public const string CONCAT = "concat";

        /// <summary>
        /// Validates a string function call and returns its field and resolved search value.
        /// </summary>
        /// <param name="nodeIn">The function call node.</param>
        /// <returns>The field being searched and the search value.</returns>
        /// <exception cref="NotSupportedException">The function or its arguments are not supported.</exception>
        public static (SingleValuePropertyAccessNode Field, string Value) GetFieldAndValue(SingleValueFunctionCallNode nodeIn)
        {
            if (nodeIn.Name is not (CONTAINS or STARTSWITH or ENDSWITH))
            {
                throw new NotSupportedException($"The function '{nodeIn.Name}' is not supported.");
            }

            QueryNode[] arguments = nodeIn.Parameters.ToArray();
            if (arguments.Length != 2 || Unwrap(arguments[0]) is not SingleValuePropertyAccessNode field)
            {
                throw new NotSupportedException($"The first argument of '{nodeIn.Name}' must be a field.");
            }

            string value = FoldString(arguments[1])
                ?? throw new NotSupportedException($"The second argument of '{nodeIn.Name}' must be a non-null string value.");

            return (field, value);
        }

        /// <summary>
        /// String functions are only supported as conditions in their own right. Comparing their
        /// result, as in <c>startswith(title, 'A') eq true</c>, is rejected rather than translated.
        /// </summary>
        /// <param name="nodeIn">The binary operator node.</param>
        /// <exception cref="NotSupportedException">A function call is an operand of a comparison.</exception>
        public static void ThrowIfFunctionIsCompared(BinaryOperatorNode nodeIn)
        {
            if (nodeIn.OperatorKind is not (BinaryOperatorKind.And or BinaryOperatorKind.Or) &&
                (Unwrap(nodeIn.Left) is SingleValueFunctionCallNode || Unwrap(nodeIn.Right) is SingleValueFunctionCallNode))
            {
                throw new NotSupportedException("A function call cannot be an operand of a comparison; use it directly as a condition.");
            }
        }

        /// <summary>
        /// Builds a LIKE pattern for the function, escaping LIKE wildcards in the value
        /// with a backslash.
        /// </summary>
        public static string GetLikePattern(string functionName, string value)
        {
            string escaped = EscapeLikeString(value);
            return functionName switch
            {
                CONTAINS => $"%{escaped}%",
                STARTSWITH => $"{escaped}%",
                ENDSWITH => $"%{escaped}",
                _ => throw new NotSupportedException($"The function '{functionName}' is not supported.")
            };
        }

        /// <summary>
        /// Escapes LIKE wildcard characters with a backslash.
        /// </summary>
        public static string EscapeLikeString(string input)
        {
            input = input.Replace(@"\", @"\\");
            input = input.Replace(@"%", @"\%");
            input = input.Replace(@"[", @"\[");
            input = input.Replace(@"]", @"\]");
            input = input.Replace(@"_", @"\_");
            return input;
        }

        /// <summary>
        /// Resolves a node to a string when it is a string constant or a concat of string constants.
        /// Returns null for anything else, including null values.
        /// </summary>
        private static string? FoldString(QueryNode node)
        {
            switch (Unwrap(node))
            {
                case ConstantNode { Value: string value }:
                    return value;
                case SingleValueFunctionCallNode { Name: CONCAT } concat:
                    QueryNode[] parts = concat.Parameters.ToArray();
                    if (parts.Length != 2)
                    {
                        return null;
                    }

                    string? left = FoldString(parts[0]);
                    string? right = FoldString(parts[1]);
                    return left is null || right is null ? null : left + right;
                default:
                    return null;
            }
        }

        private static QueryNode Unwrap(QueryNode node)
        {
            while (node is ConvertNode convertNode)
            {
                node = convertNode.Source;
            }

            return node;
        }
    }
}
