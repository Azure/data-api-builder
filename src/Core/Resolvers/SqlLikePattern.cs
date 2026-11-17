// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Where a search value must appear in a string.
    /// </summary>
    internal enum LikeMatch
    {
        Contains,
        StartsWith,
        EndsWith
    }

    /// <summary>
    /// Builds LIKE patterns with wildcards escaped by a backslash.
    /// </summary>
    internal static class SqlLikePattern
    {
        public static string Create(LikeMatch match, string value)
        {
            string escaped = Escape(value);
            return match switch
            {
                LikeMatch.Contains => $"%{escaped}%",
                LikeMatch.StartsWith => $"{escaped}%",
                LikeMatch.EndsWith => $"%{escaped}",
                _ => throw new ArgumentOutOfRangeException(nameof(match))
            };
        }

        public static string Escape(string input)
        {
            input = input.Replace(@"\", @"\\");
            input = input.Replace(@"%", @"\%");
            input = input.Replace(@"[", @"\[");
            input = input.Replace(@"]", @"\]");
            input = input.Replace(@"_", @"\_");
            return input;
        }
    }
}
