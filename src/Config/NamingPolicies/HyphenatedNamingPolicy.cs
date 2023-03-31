// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Config.NamingPolicies;

internal class HyphenatedNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        if (string.Equals(name, "graphql", StringComparison.OrdinalIgnoreCase))
        {
            return name.ToLower();
        }
        return string.Join("-", Regex.Split(name, @"(?<!^)(?=[A-Z])", RegexOptions.Compiled)).ToLower();
    }
}
