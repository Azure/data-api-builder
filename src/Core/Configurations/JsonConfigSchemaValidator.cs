// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO.Abstractions;
using System.Reflection;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.Extensions.Logging;
using NJsonSchema;
using NJsonSchema.Validation;

namespace Azure.DataApiBuilder.Core.Configurations;

public class JsonConfigSchemaValidator
{
    private ILogger<JsonConfigSchemaValidator> _logger;
    private IFileSystem _fileSystem;
    private HttpClient _httpClient;

    /// <summary> 
    /// Sets the logger, file system and httpClient for the JSON config schema validator. 
    /// </summary> 
    /// <param name="jsonSchemaValidatorLogger">The logger to use for the JSON schema validator.</param> 
    /// <param name="fileSystem">The file system to use for the JSON schema validator.</param>
    /// <param name="httpClient">The http client to use for the JSON schema validator. If not provided, a new HttpClient will be used.</param>
    public JsonConfigSchemaValidator(ILogger<JsonConfigSchemaValidator> jsonSchemaValidatorLogger, IFileSystem fileSystem, HttpClient? httpClient = null)
    {
        _logger = jsonSchemaValidatorLogger;
        _fileSystem = fileSystem;
        _httpClient = httpClient ?? new();
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
                return new(isValid: true, errors: null);
            }
            else
            {
                return new(isValid: false, errors: validationErrors);
            }
        }
        catch (Exception e)
        {
            _logger!.LogError($"Failed to validate config against schema due to \n{e.Message}");
            return new(isValid: false, errors: null);
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
                // Send a GET request to the URL specified in runtimeConfig.Schema to get the JSON schema.
                HttpResponseMessage response = await _httpClient.GetAsync(runtimeConfig.Schema);
                if (response.IsSuccessStatusCode)
                {
                    string jsonSchema = await response.Content.ReadAsStringAsync();
                    return jsonSchema;
                }
                else
                {
                    _logger!.LogWarning($"Received response: ({response.StatusCode}) while fetching schema from url: {runtimeConfig.Schema}");
                }
            }
            catch (Exception e)
            {
                _logger!.LogWarning($"Failed to send GET request to fetch schema from url: {runtimeConfig.Schema}\n{e}");
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
}
