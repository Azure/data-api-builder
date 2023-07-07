// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services.OpenAPI
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
        /// <summary>
        /// A JSON Object
        /// </summary>
        Object = 1,
        /// <summary>
        /// A JSON array
        /// </summary>
        Array = 2,
        /// <summary>
        /// A JSON string
        /// </summary>
        String = 3,
        /// <summary>
        /// A JSON number
        /// </summary>
        Number = 4,
        /// <summary>
        /// A JSON Boolean
        /// </summary>
        Boolean = 5,
        /// <summary>
        /// The JSON value null
        /// </summary>
        Null = 6
    }
}
