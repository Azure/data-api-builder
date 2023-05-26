// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityActionPolicy(string? Request = null, string? Database = null)
{
    public string ProcessedDatabaseFields()
    {
        if (Database is null)
        {
            throw new NullReferenceException("Unable to process the fields in the database policy because the policy is null.");
        }

        return ProcessFieldsInPolicy(Database);
    }

    /// <summary>
    /// Helper method which takes in the database policy and returns the processed policy
    /// without @item. directives before field names.
    /// </summary>
    /// <param name="policy">Raw database policy</param>
    /// <returns>Processed policy without @item. directives before field names.</returns>
    private static string ProcessFieldsInPolicy(string? policy)
    {
        if (policy is null)
        {
            return string.Empty;
        }

        string fieldCharsRgx = @"@item\.([a-zA-Z0-9_]*)";

        // processedPolicy would be devoid of @item. directives.
        string processedPolicy = Regex.Replace(policy, fieldCharsRgx, (columnNameMatch) =>
            columnNameMatch.Groups[1].Value
        );
        return processedPolicy;
    }
}
