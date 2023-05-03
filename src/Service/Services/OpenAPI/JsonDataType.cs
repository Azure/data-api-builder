// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Services.OpenAPI
{
    /// <summary>
    /// Specifies the data type of a JSON value.
    /// Distinguished from System.Text.Json enum JsonValueKind because there are no separate
    /// values for JsonValueKind.True or JsonValueKind.False, only a single value JsonDataType.Boolean.
    /// This distinction is necessary to facilitate OpenAPI schema creation which requires generic
    /// JSON types to be defined for parameters. Because no values are present, JsonValueKind.True/False
    /// can't be used.
    /// </summary>
    public enum JsonDataType
    {
        Undefined = 0,
        //
        // Summary:
        //     A JSON object.
        Object = 1,
        //
        // Summary:
        //     A JSON array.
        Array = 2,
        //
        // Summary:
        //     A JSON string.
        String = 3,
        //
        // Summary:
        //     A JSON number.
        Number = 4,
        //
        // Summary:
        //     A JSON Boolean
        Boolean = 5,
        //
        // Summary:
        //     The JSON value null.
        Null = 6
    }
}
