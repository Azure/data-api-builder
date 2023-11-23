// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Reflection;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Azure.DataApiBuilder.Core.Configurations;

public class JsonConfigSchemaValidator
{
    private ILogger<JsonConfigSchemaValidator>? _logger;
    private IFileSystem _fileSystem = new FileSystem();

    /// <summary> 
    /// Sets the logger and file system for the JSON config schema validator. 
    /// </summary> 
    /// <param name="jsonSchemaValidatorLogger">The logger to use for the JSON schema validator.</param> 
    /// <param name="fileSystem">The file system to use for the JSON schema validator.</param>

    public JsonConfigSchemaValidator(ILogger<JsonConfigSchemaValidator> jsonSchemaValidatorLogger, IFileSystem fileSystem)
    {
        _logger = jsonSchemaValidatorLogger;
        _fileSystem = fileSystem;
    }

    /// <summary> 
    /// Validates a JSON schema against JSON data. 
    /// </summary> 
    /// <param name="jsonSchema">The JSON schema raw content to validate against.</param> 
    /// <param name="jsonData">The JSON data to validate.</param> 
    /// <returns>A tuple containing a boolean indicating
    /// if the validation was successful and a collection of validation errors if there were any.</returns> 
    public async Task<JsonSchemaValidationResult> ValidateJsonConfigWithSchemaAsync(string jsonSchema, string jsonData)
    {
        try
        {
            JsonSchema schema = await JsonSchema.FromJsonAsync(jsonSchema);
            ICollection<ValidationError> validationErrors = schema.Validate(jsonData, SchemaType.JsonSchema);

            if (!validationErrors.Any())
            {
                _logger!.LogInformation("The config satisfies the schema requirements.");
                return new(true, null);
            }
            else
            {
                return new(false, validationErrors);
            }
        }
        catch (Exception e)
        {
            _logger!.LogError($"Failed to validate config against schema due to \n{e.Message}");
            return new(false, null);
        }
    }

    /// <summary>
    /// Retrieves the JSON schema for validation from the provided runtime config or the assembly package. 
    /// </summary> 
    /// <param name="runtimeConfig">The runtimeConfig object containing the schema information.</param> 
    /// <returns>The JSON schema as a string, or null if the schema cannot be obtained.</returns> 
    public async Task<string?> GetJsonSchema(RuntimeConfig runtimeConfig)
    {
        // DEFAULT_CONFIG_SCHEMA_LINK is just a placeholder with no actual schema, hence should not be used to fetch the schema.
        if (!string.IsNullOrWhiteSpace(runtimeConfig.Schema) && !runtimeConfig.Schema.Equals(RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK))
        {
            try
            {
                JsonSchema jsonSchema = await JsonSchema.FromUrlAsync(runtimeConfig.Schema);
                return jsonSchema.ToJson();
            }
            catch (Exception e)
            {
                _logger!.LogError($"Failed to get schema from url: {runtimeConfig.Schema}\n{e}");
            }
        }

        try
        {
            return GetSchemaFromAssemblyPackage();
        }
        catch (Exception e)
        {
            _logger!.LogError($"Failed to get schema from assembly package\n{e}");
        }

        return null;
    }

    /// <summary> 
    /// Retrieves the JSON schema from the assembly package. 
    /// </summary>
    /// <returns>The contents of the JSON schema file.</returns> 
    private string GetSchemaFromAssemblyPackage()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string directoryPath = _fileSystem.Path.GetDirectoryName(assemblyPath)!;
        string jsonPath = _fileSystem.Path.Combine(directoryPath, FileSystemRuntimeConfigLoader.SCHEMA);

        string contents = File.ReadAllText(jsonPath);
        return contents;
    }

    /// <summary>
    /// Class to hold the result of a JSON schema validation.
    /// </summary>
    public class JsonSchemaValidationResult
    {
        public bool IsValid;
        public ICollection<ValidationError>? ValidationErrors;

        public int ErrorCount;

        public string ErrorMessage;

        public JsonSchemaValidationResult(bool isValid, ICollection<ValidationError>? errors)
        {
            IsValid = isValid;
            ValidationErrors = errors;
            ErrorCount = errors?.Count ?? 0;
            ErrorMessage = errors is null ? string.Empty : FormatSchemaValidationErrorMessage(errors);
        }

        private static string FormatSchemaValidationErrorMessage(ICollection<ValidationError> validationErrors)
        {
            return $"> Total schema validation errors: {validationErrors.Count}\n" +
                string.Join("", validationErrors.Select(e => $"> {e} at " +
                $"{e.LineNumber}:{e.LinePosition}\n\n"));
        }
    }
}
