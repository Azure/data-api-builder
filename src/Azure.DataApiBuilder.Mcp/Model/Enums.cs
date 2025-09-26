// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Mcp.Model
{
    public class McpEnums
    {
        /// <summary>
        /// Specifies the type of tool.
        /// </summary>
        /// <remarks>This enumeration defines whether a tool is a built-in tool provided by the system  or
        /// a custom tool defined by the user.</remarks>
        public enum ToolType
        {
            BuiltIn,
            Custom
        }
    }
}
