// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.Converters;

/// <summary>
/// Control how to handle environment variable replacement failures when deserializing strings in the JSON config file.
/// </summary>
public enum EnvironmentVariableReplacementFailureMode
{
    /// <summary>
    /// Ignore the missing environment variable and return the original value, eg: @env('schema').
    /// </summary>
    Ignore,
    /// <summary>
    /// Throw an exception when a missing environment variable is encountered. This is the default behavior.
    /// </summary>
    Throw
}
