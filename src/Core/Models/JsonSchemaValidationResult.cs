// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Class to hold the result of a JSON schema validation.
/// </summary>
public class JsonSchemaValidationResult
{
    public bool IsValid { get; private set; }
    public IReadOnlyDictionary<string, string>? ValidationErrors { get; private set; }

    public int ErrorCount { get; private set; }

    public string ErrorMessage { get; private set; }

    public JsonSchemaValidationResult(bool isValid, IReadOnlyDictionary<string, string>? errors)
    {
        IsValid = isValid;
        ValidationErrors = errors;
        ErrorCount = errors?.Count ?? 0;
        ErrorMessage = errors is null ? string.Empty : FormatSchemaValidationErrorMessage(errors);
    }

    /// <summary>
    /// It formats and returns a string that includes the total count of validation errors
    /// and details of each error. The details of each error include the
    /// error message, line number, and line position where the error occurred.
    /// </summary>
    /// <param name="validationErrors">list of schema validation errors</param>
    private static string FormatSchemaValidationErrorMessage(IReadOnlyDictionary<string, string> validationErrors)
    {
        return $"> Total schema validation errors: {validationErrors.Count}";
    }
}
