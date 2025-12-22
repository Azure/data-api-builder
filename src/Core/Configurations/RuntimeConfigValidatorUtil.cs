// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace Azure.DataApiBuilder.Core.Configurations;

/// <summary>
/// This class encapsulates methods to validate the runtime config file.
/// </summary>
public static class RuntimeConfigValidatorUtil
{
    // Reserved characters as defined in RFC3986 are not allowed to be present in the
    // REST/GraphQL custom path because they are not acceptable to be present in URIs.
    // Refer here: https://www.rfc-editor.org/rfc/rfc3986#page-12.
    private static readonly string _reservedUriChars = @"[\.:\?#/\[\]@!$&'()\*\+,;=]+";

    //  Regex to validate rest/graphql custom path prefix.
    private static readonly Regex _reservedUriCharsRgx = new(_reservedUriChars, RegexOptions.Compiled);

    public const string URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG = "contains one or more reserved characters.";

    /// <summary>
    /// Method to validate that the REST/GraphQL URI component is well formed such that it does not contain
    /// any reserved characters or spaces. In case the URI component is not well formed the exception message containing
    /// the reason for ill-formed URI component is returned. Else we return an empty string.
    /// </summary>
    /// <param name="uriComponent">path prefix/base route for rest/graphql apis</param>
    /// <returns>false when the URI component is not well formed.</returns>
    public static bool TryValidateUriComponent(string? uriComponent, out string exceptionMessageSuffix)
    {
        exceptionMessageSuffix = string.Empty;
        if (string.IsNullOrEmpty(uriComponent))
        {
            exceptionMessageSuffix = "cannot be null or empty.";
        }
        // A valid URI component should start with a forward slash '/'.
        else if (!uriComponent.StartsWith("/"))
        {
            exceptionMessageSuffix = "should start with a '/'.";
        }
        else if (uriComponent.Any(x => Char.IsWhiteSpace(x)))
        {
            exceptionMessageSuffix = "contains white spaces.";
        }
        else
        {
            uriComponent = uriComponent.Substring(1);
            // URI component should not contain any reserved characters.
            if (DoesUriComponentContainReservedChars(uriComponent))
            {
                exceptionMessageSuffix = URI_COMPONENT_WITH_RESERVED_CHARS_ERR_MSG;
            }
        }

        return string.IsNullOrEmpty(exceptionMessageSuffix);
    }

    /// <summary>
    /// Method to validate that the REST/GraphQL API's URI component does not contain
    /// any reserved characters.
    /// </summary>
    /// <param name="uriComponent">path prefix for rest/graphql apis</param>
    public static bool DoesUriComponentContainReservedChars(string uriComponent)
    {
        return _reservedUriCharsRgx.IsMatch(uriComponent);
    }

    /// <summary>
    /// Method to validate an entity REST path allowing sub-directories (forward slashes).
    /// Each segment of the path is validated for reserved characters.
    /// </summary>
    /// <param name="entityRestPath">The entity REST path to validate.</param>
    /// <param name="errorMessage">Output parameter containing a specific error message if validation fails.</param>
    /// <returns>true if the path is valid, false otherwise.</returns>
    public static bool TryValidateEntityRestPath(string entityRestPath, out string? errorMessage)
    {
        errorMessage = null;

        // Check for backslash usage - common mistake
        if (entityRestPath.Contains('\\'))
        {
            errorMessage = "contains a backslash (\\). Use forward slash (/) for path separators.";
            return false;
        }

        // Check for whitespace
        if (entityRestPath.Any(char.IsWhiteSpace))
        {
            errorMessage = "contains whitespace which is not allowed in URL paths.";
            return false;
        }

        // Split the path by '/' to validate each segment separately
        string[] segments = entityRestPath.Split('/');

        // Validate each segment doesn't contain reserved characters
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment))
            {
                errorMessage = "contains empty path segments. Ensure there are no leading, consecutive, or trailing slashes.";
                return false;
            }

            // Check for specific reserved characters and provide helpful messages
            if (segment.Contains('?'))
            {
                errorMessage = "contains '?' which is reserved for query strings in URLs.";
                return false;
            }

            if (segment.Contains('#'))
            {
                errorMessage = "contains '#' which is reserved for URL fragments.";
                return false;
            }

            if (segment.Contains(':'))
            {
                errorMessage = "contains ':' which is reserved for port numbers in URLs.";
                return false;
            }

            if (_reservedUriCharsRgx.IsMatch(segment))
            {
                errorMessage = "contains reserved characters that are not allowed in URL paths.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Method to validate if the TTL passed by the user is valid
    /// </summary>
    /// <param name="ttl">Time to Live</param>
    public static bool IsTTLValid(int ttl)
    {
        if (ttl > 0)
        {
            return true;
        }

        return false;
    }
}
