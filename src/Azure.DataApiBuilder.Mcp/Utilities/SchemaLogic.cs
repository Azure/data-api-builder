// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Utilities;

/// <summary>
/// Provides GraphQL schema analysis and entity metadata extraction functionality
/// </summary>
public class SchemaLogic
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SchemaLogic> _logger;
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly IAuthorizationResolver _authorizationResolver;

    public SchemaLogic(IServiceProvider services)
    {
        _services = services;
        _logger = services.GetRequiredService<ILogger<SchemaLogic>>();
        _runtimeConfigProvider = services.GetRequiredService<RuntimeConfigProvider>();
        _authorizationResolver = services.GetRequiredService<IAuthorizationResolver>();
    }

    /// <summary>
    /// Gets the raw GraphQL schema as ISchema object
    /// </summary>
    public async Task<ISchema> GetRawGraphQLSchemaAsync()
    {
        IRequestExecutorResolver? requestExecutorResolver = _services.GetService<IRequestExecutorResolver>();

        if (requestExecutorResolver == null)
        {
            throw new InvalidOperationException("IRequestExecutorResolver not available");
        }

        IRequestExecutor requestExecutor = await requestExecutorResolver.GetRequestExecutorAsync();
        return requestExecutor.Schema;
    }

    /// <summary>
    /// Gets the GraphQL schema as SDL string
    /// </summary>
    public async Task<string> GetGraphQLSchemaStringAsync()
    {
        ISchema schema = await GetRawGraphQLSchemaAsync();
        return schema.ToString();
    }

    /// <summary>
    /// Generates entity metadata as JSON
    /// </summary>
    /// <param name="nameOnly">If true, returns only name and description for each entity</param>
    /// <param name="entityNames">Specific entity names to include. If null, includes all entities. If empty array, throws error.</param>
    /// <returns>JSON string containing entity metadata</returns>
    public async Task<string> GetEntityMetadataAsJsonAsync(bool nameOnly = false, string[]? entityNames = null)
    {
        // Validate input parameters
        if (entityNames is not null && entityNames.Length == 0)
        {
            throw new ArgumentException("Entity names array cannot be empty. Either provide specific entity names or pass null for all entities.", nameof(entityNames));
        }

        ISchema schema = await GetRawGraphQLSchemaAsync();
        List<object> entityMetadataList = new();

        // Get all object types from the schema that have @model directive
        List<KeyValuePair<string, IObjectType>> entityTypes = GetEntityTypesFromSchema(schema);

        // Filter by requested entity names if specified
        if (entityNames != null)
        {
            entityTypes = entityTypes.Where(kvp => entityNames.Contains(kvp.Key)).ToList();
        }

        foreach ((string entityName, IObjectType objectType) in entityTypes)
        {
            try
            {
                object? entityMetadata = await BuildEntityMetadataFromSchema(entityName, objectType, nameOnly);

                if (entityMetadata != null)
                {
                    entityMetadataList.Add(entityMetadata);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process entity {EntityName}", entityName);
                // Continue processing other entities
            }
        }

        return JsonSerializer.Serialize(entityMetadataList, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// Gets entity types from the GraphQL schema that have @model directive
    /// </summary>
    private static List<KeyValuePair<string, IObjectType>> GetEntityTypesFromSchema(ISchema schema)
    {
        List<KeyValuePair<string, IObjectType>> entityTypes = new();

        foreach (INamedType type in schema.Types)
        {
            if (type is IObjectType objectType &&
                objectType.Directives.Any(d => d.Type.Name == "model") &&
                !IsSystemType(objectType.Name))
            {
                // Get entity name from @model directive or use type name
                string entityName = GetEntityNameFromModelDirective(objectType) ?? objectType.Name;
                entityTypes.Add(new KeyValuePair<string, IObjectType>(entityName, objectType));
            }
        }

        return entityTypes;
    }

    /// <summary>
    /// Checks if this is a system type that should be excluded
    /// </summary>
    private static bool IsSystemType(string typeName)
    {
        // Exclude system types like Query, Mutation, Connection types, etc.
        return typeName is "Query" or "Mutation" or "DbOperationResult" ||
               typeName.EndsWith("Connection") ||
               typeName.EndsWith("Aggregations") ||
               typeName.EndsWith("GroupBy") ||
               typeName.EndsWith("FilterInput") ||
               typeName.EndsWith("OrderByInput") ||
               typeName.EndsWith("Input");
    }

    /// <summary>
    /// Gets entity name from @model directive
    /// </summary>
    private static string? GetEntityNameFromModelDirective(IObjectType objectType)
    {
        Directive? modelDirective = objectType.Directives.FirstOrDefault(d => d.Type.Name == "model");
        if (modelDirective != null)
        {
            // Try to get the name argument from the directive
            // For HotChocolate, we need to access directive values differently
            try
            {
                // Try to get the literal value of the name argument
                HotChocolate.Language.ArgumentNode? nameArgument = modelDirective.AsSyntaxNode().Arguments.FirstOrDefault(a => a.Name.Value == "name");

                if (nameArgument?.Value is HotChocolate.Language.StringValueNode stringValue)
                {
                    return stringValue.Value;
                }
            }
            catch
            {
                // If we can't get the directive value, fall back to type name
            }
        }

        return null;
    }

    /// <summary>
    /// Builds metadata for a single entity from GraphQL schema
    /// </summary>
    private async Task<object?> BuildEntityMetadataFromSchema(string entityName, IObjectType objectType, bool nameOnly)
    {
        string description = GetEntityDescriptionFromSchema(entityName, objectType);

        if (nameOnly)
        {
            return new
            {
                Name = entityName,
                Description = description
            };
        }

        // Build complete metadata from schema
        List<object> keys = BuildPrimaryKeysFromSchema(objectType);
        List<object> fields = BuildFieldsFromSchema(objectType);
        List<object> allowedActions = await BuildAllowedActionsFromSchema(entityName, objectType);

        return new
        {
            Name = entityName,
            Description = description,
            Keys = keys,
            Fields = fields,
            AllowedActions = allowedActions
        };
    }

    /// <summary>
    /// Gets entity description from GraphQL schema
    /// </summary>
    private string GetEntityDescriptionFromSchema(string entityName, IObjectType objectType)
    {
        // Check if there's a description in the type definition
        if (!string.IsNullOrEmpty(objectType.Description))
        {
            return objectType.Description;
        }

        // Check if this is a stored procedure (has execute mutations)
        bool isStoredProcedure = IsStoredProcedureType(entityName);

        return isStoredProcedure
            ? $"Represents the {entityName} stored procedure"
            : $"Represents a {entityName} entity in the system";
    }

    /// <summary>
    /// Checks if this entity represents a stored procedure
    /// </summary>
    private bool IsStoredProcedureType(string entityName)
    {
        try
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();

            if (runtimeConfig.Entities.TryGetValue(entityName, out Entity? entity))
            {
                return entity.Source.Type == EntitySourceType.StoredProcedure;
            }
        }
        catch
        {
            // If we can't determine from config, check schema patterns
        }

        // Fallback: check if mutations contain execute operations for this entity
        return false; // We'll implement this if needed
    }

    /// <summary>
    /// Builds primary key information from GraphQL schema
    /// </summary>
    private static List<object> BuildPrimaryKeysFromSchema(IObjectType objectType)
    {
        List<object> keys = new();

        foreach (IObjectField field in objectType.Fields)
        {
            Directive? primaryKeyDirective = field.Directives.FirstOrDefault(d => d.Type.Name == "primaryKey");

            if (primaryKeyDirective != null)
            {
                bool isAutoGenerated = field.Directives.Any(d => d.Type.Name == "autoGenerated");
                string databaseType = GetDatabaseTypeFromDirective(primaryKeyDirective);

                keys.Add(new
                {
                    field.Name,
                    Type = MapGraphQLTypeToString(field.Type),
                    Autogen = isAutoGenerated,
                    Description = $"Primary key field for {objectType.Name}",
                    //DatabaseType = databaseType
                });
            }
        }

        return keys;
    }

    /// <summary>
    /// Gets database type from @primaryKey directive
    /// </summary>
    private static string GetDatabaseTypeFromDirective(Directive primaryKeyDirective)
    {
        // Try to get the databaseType argument from the directive
        try
        {
            HotChocolate.Language.ArgumentNode? databaseTypeArgument = primaryKeyDirective.AsSyntaxNode().Arguments.FirstOrDefault(a => a.Name.Value == "databaseType");

            if (databaseTypeArgument?.Value is HotChocolate.Language.StringValueNode stringValue)
            {
                return stringValue.Value;
            }
        }
        catch
        {
            // If we can't get the directive value, return unknown
        }

        return "Unknown";
    }

    /// <summary>
    /// Builds field information from GraphQL schema
    /// </summary>
    private static List<object> BuildFieldsFromSchema(IObjectType objectType)
    {
        List<object> fields = new();
        bool isStoredProcedure = IsStoredProcedureFromType(objectType);

        foreach ((IObjectField field, int Index) value in objectType.Fields.Select((x, i) => (x, i)))
        {
            // Skip __typename field if it is the first field, it usually is
            if (value.Index == 0 && value.field.Name == "__typename")
            {
                continue;
            }

            // Skip primary key fields as they're already in the keys section
            bool isPrimaryKey = value.field.Directives.Any(d => d.Type.Name == "primaryKey");

            if (isPrimaryKey)
            {
                continue;
            }

            // Skip relationship fields (they have @relationship directive)
            bool isRelationship = value.field.Directives.Any(d => d.Type.Name == "relationship");

            if (isRelationship)
            {
                continue;
            }

            bool isAutoGenerated = value.field.Directives.Any(d => d.Type.Name == "autoGenerated");
            bool hasDefaultValue = value.field.Directives.Any(d => d.Type.Name == "defaultValue");
            bool isNullable = IsNullableType(value.field.Type);

            fields.Add(new
            {
                value.field.Name,
                Type = MapGraphQLTypeToString(value.field.Type),
                Nullable = isNullable,
                HasDefault = hasDefaultValue,
                ReadOnly = isStoredProcedure || isAutoGenerated, // All stored procedure fields arereadonly
                Description = value.field.Description ?? $"Field {value.field.Name} of type {GetBaseTypeName(value.field.Type)}"
            });
        }

        return fields;
    }

    /// <summary>
    /// Checks if this object type represents a stored procedure based on its name patterns
    /// </summary>
    private static bool IsStoredProcedureFromType(IObjectType objectType)
    {
        // Stored procedures typically don't have Connection, Aggregations, GroupBy suffixes
        // and are often named with patterns that suggest they're procedures
        string typeName = objectType.Name;

        return !IsSystemType(typeName) &&
               !typeName.EndsWith("Connection") &&
               !typeName.EndsWith("Aggregations") &&
               !typeName.EndsWith("GroupBy") &&
               (typeName.StartsWith("Get") || typeName.StartsWith("Execute") || typeName.Contains("Procedure"));
    }

    /// <summary>
    /// Builds allowed actions from GraphQL schema and runtime config
    /// </summary>
    private async Task<List<object>> BuildAllowedActionsFromSchema(string entityName, IObjectType objectType)
    {
        List<object> allowedActions = new();
        Dictionary<string, EntityMetadata>? entityPermissionsMap = _authorizationResolver.EntityPermissionsMap;

        // Determine entity type to know which operations to check
        bool isStoredProcedure = IsStoredProcedureType(entityName);

        List<EntityActionOperation> operationsToCheck = isStoredProcedure
            ? new() { EntityActionOperation.Execute }
            : new() { EntityActionOperation.Create, EntityActionOperation.Read, EntityActionOperation.Update, EntityActionOperation.Delete };

        foreach (EntityActionOperation operation in operationsToCheck)
        {
            IEnumerable<string> allowedRoles = IAuthorizationResolver.GetRolesForOperation(entityName, operation, entityPermissionsMap);

            if (allowedRoles.Any())
            {
                object actionMetadata = await BuildActionMetadataFromSchema(operation, entityName, objectType);
                allowedActions.Add(actionMetadata);
            }
        }

        return allowedActions;
    }

    /// <summary>
    /// Builds action metadata from GraphQL schema
    /// </summary>
    private async Task<object> BuildActionMetadataFromSchema(EntityActionOperation operation, string entityName, IObjectType objectType)
    {
        string actionName = GetActionName(operation);
        object parameters = await BuildActionParametersFromSchema(operation, entityName, objectType);
        object response = BuildActionResponseFromSchema(operation, objectType);

        // Create a dictionary to return the action with dynamic key
        Dictionary<string, object> actionDict = new()
        {
            [actionName] = new
            {
                Parameters = parameters,
                Response = response
            }
        };

        return actionDict;
    }

    /// <summary>
    /// Gets the action name for the operation
    /// </summary>
    private static string GetActionName(EntityActionOperation operation)
    {
        return operation switch
        {
            EntityActionOperation.Create => "create_entity_record",
            EntityActionOperation.Read => "read_entity_records",
            EntityActionOperation.Update => "update_entity_record",
            EntityActionOperation.Delete => "delete_entity_record",
            EntityActionOperation.Execute => "execute_entity",
            _ => throw new ArgumentException($"Unknown operation: {operation}")
        };
    }

    /// <summary>
    /// Builds parameters from GraphQL schema
    /// </summary>
    private async Task<object> BuildActionParametersFromSchema(EntityActionOperation operation, string entityName, IObjectType objectType)
    {
        switch (operation)
        {
            case EntityActionOperation.Create:
                return new
                {
                    Entity = "String!",
                    Fields = await GetCreateFieldParametersFromSchema(entityName)
                };

            case EntityActionOperation.Update:
                return new
                {
                    Entity = "String!",
                    Fields = await GetUpdateFieldParametersFromSchema(entityName)
                };

            case EntityActionOperation.Read:
                return new
                {
                    Entity = "String!",
                    Filters = "Object",
                    First = "Int",
                    After = "String"
                };

            case EntityActionOperation.Delete:
                return new
                {
                    Entity = "String!",
                    Id = GetPrimaryKeyParametersFromSchema(objectType)
                };

            case EntityActionOperation.Execute:
                return new
                {
                    Entity = "String!",
                    Fields = await GetStoredProcedureParametersFromSchema(entityName)
                };

            default:
                return new { };
        }
    }

    /// <summary>
    /// Builds response from GraphQL schema
    /// </summary>
    private static object BuildActionResponseFromSchema(EntityActionOperation operation, IObjectType objectType)
    {
        List<string> responseFields = GetResponseFieldsFromSchema(objectType);

        return operation switch
        {
            EntityActionOperation.Read => new { Items = responseFields },
            EntityActionOperation.Execute => responseFields, // Could be array or single
            _ => responseFields // Single object response
        };
    }

    /// <summary>
    /// Gets create field parameters from schema by examining the CreateXxxInput type
    /// </summary>
    private async Task<List<string>> GetCreateFieldParametersFromSchema(string entityName)
    {
        ISchema schema = await GetRawGraphQLSchemaAsync();
        string createInputTypeName = $"Create{entityName}Input";

        return GetFieldsFromInputType(schema, createInputTypeName);
    }

    /// <summary>
    /// Gets update field parameters from schema by examining the UpdateXxxInput type
    /// </summary>
    private async Task<List<string>> GetUpdateFieldParametersFromSchema(string entityName)
    {
        ISchema schema = await GetRawGraphQLSchemaAsync();
        string updateInputTypeName = $"Update{entityName}Input";

        return GetFieldsFromInputType(schema, updateInputTypeName);
    }

    /// <summary>
    /// Gets stored procedure parameters from schema by examining the mutation field arguments
    /// </summary>
    private async Task<List<string>> GetStoredProcedureParametersFromSchema(string entityName)
    {
        List<string> parameters = new();

        try
        {
            ISchema schema = await GetRawGraphQLSchemaAsync();

            // Find the Mutation type
            if (schema.Types.FirstOrDefault(t => t.Name == "Mutation") is IObjectType mutationType)
            {
                // Look for execute mutation for this entity
                string expectedMutationName = $"execute{entityName}";
                IObjectField? mutationField = mutationType.Fields.FirstOrDefault(f =>
                    f.Name.Equals(expectedMutationName, StringComparison.OrdinalIgnoreCase));

                if (mutationField != null)
                {
                    foreach (IInputField argument in mutationField.Arguments)
                    {
                        // Skip common arguments like 'item' and focus on direct parameters
                        if (argument.Name != "item" && argument.Name != "entity")
                        {
                            parameters.Add($"{argument.Name}: {MapGraphQLTypeToString(argument.Type)}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stored procedure parameters for {EntityName}", entityName);
        }

        return parameters;
    }

    /// <summary>
    /// Helper method to extract field names from an input type
    /// </summary>
    private static List<string> GetFieldsFromInputType(ISchema schema, string inputTypeName)
    {
        List<string> fields = new();

        if (schema.Types.FirstOrDefault(t => t.Name == inputTypeName) is IInputObjectType inputType)
        {
            foreach (IInputField field in inputType.Fields)
            {
                fields.Add(field.Name);
            }
        }

        return fields;
    }

    /// <summary>
    /// Maps GraphQL type to string representation
    /// </summary>
    private static string MapGraphQLTypeToString(IType type, bool forceRequired = false)
    {
        string baseType = GetBaseTypeName(type);
        bool isRequired = forceRequired || IsRequiredType(type);

        return isRequired ? $"{baseType}!" : baseType;
    }

    /// <summary>
    /// Gets the base type name from a GraphQL type
    /// </summary>
    private static string GetBaseTypeName(IType type)
    {
        return type switch
        {
            NonNullType nonNull => GetBaseTypeName(nonNull.Type),
            ListType list => $"[{GetBaseTypeName(list.ElementType)}]",
            INamedType named => MapScalarTypeName(named.Name),
            _ => type.ToString() ?? "Unknown"
        };
    }

    /// <summary>
    /// Maps GraphQL scalar type names to simplified names
    /// </summary>
    private static string MapScalarTypeName(string typeName)
    {
        return typeName switch
        {
            "String" => "String",
            "Int" => "Int",
            "Long" => "Long",
            "Short" => "Short",
            "Byte" => "Byte",
            "Boolean" => "Boolean",
            "Float" => "Float",
            "Single" => "Float",
            "Decimal" => "Decimal",
            "DateTime" => "DateTime",
            "UUID" => "UUID",
            "LocalTime" => "LocalTime",
            "ByteArray" => "ByteArray",
            _ => typeName
        };
    }

    /// <summary>
    /// Checks if a GraphQL type is nullable
    /// </summary>
    private static bool IsNullableType(IType type)
    {
        return type is not NonNullType;
    }

    /// <summary>
    /// Checks if a GraphQL type is required (non-null)
    /// </summary>
    private static bool IsRequiredType(IType type)
    {
        return type is NonNullType;
    }

    /// <summary>
    /// Gets response fields from schema
    /// </summary>
    private static List<string> GetResponseFieldsFromSchema(IObjectType objectType)
    {
        List<string> fields = new();

        foreach (IObjectField field in objectType.Fields)
        {
            // Skip __typename field
            if (field.Name == "__typename")
            {
                continue;
            }

            // Skip relationship fields for response (they're handled separately)
            bool isRelationship = field.Directives.Any(d => d.Type.Name == "relationship");
            if (!isRelationship)
            {
                fields.Add(field.Name);
            }
        }

        return fields;
    }

    /// <summary>
    /// Gets primary key parameters from schema
    /// </summary>
    private static object GetPrimaryKeyParametersFromSchema(IObjectType objectType)
    {
        List<IObjectField> primaryKeys = objectType.Fields.Where(f => f.Directives.Any(d => d.Type.Name == "primaryKey")).ToList();

        if (primaryKeys.Count == 1)
        {
            IObjectField pkField = primaryKeys[0];

            return $"{pkField.Name}: {MapGraphQLTypeToString(pkField.Type, forceRequired: true)}";
        }

        // Multiple primary keys
        List<string> keyFields = new();
        foreach (IObjectField pkField in primaryKeys)
        {
            keyFields.Add($"{pkField.Name}: {MapGraphQLTypeToString(pkField.Type, forceRequired: true)}");
        }

        return keyFields;
    }
}
