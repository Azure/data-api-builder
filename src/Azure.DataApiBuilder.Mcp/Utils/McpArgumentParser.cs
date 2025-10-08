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
        /// Parses entity and keys arguments for delete/update operations.
        /// </summary>
        public static bool TryParseEntityAndKeys(
            JsonElement root,
            out string entityName,
            out Dictionary<string, object?> keys,
            out string error)
        {
            entityName = string.Empty;
            keys = new Dictionary<string, object?>();
            error = string.Empty;

            if (!root.TryGetProperty("entity", out JsonElement entityEl) ||
                !root.TryGetProperty("keys", out JsonElement keysEl))
            {
                error = "Missing required arguments 'entity' or 'keys'.";
                return false;
            }

            // Parse and validate entity name
            entityName = entityEl.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entityName))
            {
                error = "Entity is required";
                return false;
            }

            // Parse and validate keys
            if (keysEl.ValueKind != JsonValueKind.Object)
            {
                error = "'keys' must be a JSON object.";
                return false;
            }

            try
            {
                keys = JsonSerializer.Deserialize<Dictionary<string, object?>>(keysEl.GetRawText()) ?? new Dictionary<string, object?>();
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
                fields = JsonSerializer.Deserialize<Dictionary<string, object?>>(fieldsEl.GetRawText()) ?? new Dictionary<string, object?>();
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
    }
}
