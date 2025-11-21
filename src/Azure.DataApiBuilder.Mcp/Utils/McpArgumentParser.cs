// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Utility class for parsing MCP tool arguments.
    /// </summary>
    public static class McpArgumentParser
    {
        /// <summary>
        /// Parses only the entity name from arguments.
        /// </summary>
        public static bool TryParseEntity(
            JsonElement root,
            out string entityName,
            out string error)
        {
            entityName = string.Empty;
            error = string.Empty;

            if (!root.TryGetProperty("entity", out JsonElement entityEl))
            {
                error = "Missing required argument 'entity'.";
                return false;
            }

            entityName = entityEl.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entityName))
            {
                error = "Entity is required";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses entity and data arguments for create operations.
        /// </summary>
        public static bool TryParseEntityAndData(
            JsonElement root,
            out string entityName,
            out JsonElement dataElement,
            out string error)
        {
            dataElement = default;

            if (!TryParseEntity(root, out entityName, out error))
            {
                return false;
            }

            if (!root.TryGetProperty("data", out dataElement))
            {
                error = "Missing required argument 'data'.";
                return false;
            }

            if (dataElement.ValueKind != JsonValueKind.Object)
            {
                error = "'data' must be a JSON object.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses entity and keys arguments for delete/update operations.
        /// </summary>
        public static bool TryParseEntityAndKeys(
            JsonElement root,
            out string entityName,
            out Dictionary<string, object?> keys,
            out string error)
        {
            keys = new Dictionary<string, object?>();
            if (!TryParseEntity(root, out entityName, out error))
            {
                return false;
            }

            if (!root.TryGetProperty("keys", out JsonElement keysEl))
            {
                error = "Missing required argument 'keys'.";
                return false;
            }

            if (keysEl.ValueKind != JsonValueKind.Object)
            {
                error = "'keys' must be a JSON object.";
                return false;
            }

            try
            {
                keys = JsonSerializer.Deserialize<Dictionary<string, object?>>(keysEl) ?? new Dictionary<string, object?>();
            }
            catch (Exception ex)
            {
                error = $"Failed to parse 'keys': {ex.Message}";
                return false;
            }

            if (keys.Count == 0)
            {
                error = "Keys are required";
                return false;
            }

            // Validate key values
            foreach (KeyValuePair<string, object?> kv in keys)
            {
                if (kv.Value is null || (kv.Value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    error = $"Primary key value for '{kv.Key}' cannot be null or empty";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Parses entity, keys, and fields arguments for update operations.
        /// </summary>
        public static bool TryParseEntityKeysAndFields(
            JsonElement root,
            out string entityName,
            out Dictionary<string, object?> keys,
            out Dictionary<string, object?> fields,
            out string error)
        {
            fields = new Dictionary<string, object?>();

            // First parse entity and keys
            if (!TryParseEntityAndKeys(root, out entityName, out keys, out error))
            {
                return false;
            }

            // Then parse fields
            if (!root.TryGetProperty("fields", out JsonElement fieldsEl))
            {
                error = "Missing required argument 'fields'.";
                return false;
            }

            if (fieldsEl.ValueKind != JsonValueKind.Object)
            {
                error = "'fields' must be a JSON object.";
                return false;
            }

            try
            {
                fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldsEl) ?? new Dictionary<string, object?>();
            }
            catch (Exception ex)
            {
                error = $"Failed to parse 'fields': {ex.Message}";
                return false;
            }

            if (fields.Count == 0)
            {
                error = "At least one field must be provided";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Parses the execute arguments from the JSON input.
        /// </summary>
        public static bool TryParseExecuteArguments(
            JsonElement rootElement,
            out string entity,
            out Dictionary<string, object?> parameters,
            out string parseError)
        {
            entity = string.Empty;
            parameters = new Dictionary<string, object?>();

            if (rootElement.ValueKind != JsonValueKind.Object)
            {
                parseError = "Arguments must be an object";
                return false;
            }

            if (!TryParseEntity(rootElement, out entity, out parseError))
            {
                return false;
            }

            // Extract parameters if provided (optional)
            if (rootElement.TryGetProperty("parameters", out JsonElement parametersElement) &&
                parametersElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in parametersElement.EnumerateObject())
                {
                    parameters[property.Name] = GetExecuteParameterValue(property.Value);
                }
            }

            return true;
        }

        // Local helper replicating ExecuteEntityTool.GetParameterValue without refactoring other tools.
        private static object? GetExecuteParameterValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number =>
                    element.TryGetInt64(out long longValue) ? longValue :
                    element.TryGetDecimal(out decimal decimalValue) ? decimalValue :
                    element.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }
    }
}
