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
    private static ILogger<JsonSchemaValidator>? _logger = new LoggerFactory().CreateLogger<JsonSchemaValidator>();
    private static IFileSystem _fileSystem = new FileSystem();

    /// <summary> 
    /// Sets the logger and file system for the JSON config schema validator. 
    /// </summary> 
    /// <param name="jsonSchemaValidatorLogger">The logger to use for the JSON schema validator.</param> 
    /// <param name="fileSystem">The file system to use for the JSON schema validator.</param>
    public static void SetLoggerAndFileSystemForJsonConfigSchemaValidator(
        ILogger<JsonSchemaValidator> jsonSchemaValidatorLogger,
        IFileSystem fileSystem)
    {
        _logger = jsonSchemaValidatorLogger;
        _fileSystem = fileSystem;
    }

     /// <summary> 
    /// Validates a JSON schema against JSON data. 
    /// </summary> 
    /// <param name="jsonSchema">The JSON schema to validate against.</param> 
    /// <param name="jsonData">The JSON data to validate.</param> 
    /// <returns>A tuple containing a boolean indicating
    /// if the validation was successful and a collection of validation errors if there were any.</returns> 
    public static async Task<(bool ,ICollection<ValidationError>?)> ValidateJsonConfigWithSchemaAsync(string? jsonSchema, string? jsonData)
    {
        try{
            JsonSchema schema = await JsonSchema.FromJsonAsync(jsonSchema);
            ICollection<ValidationError> validationErrors = schema.Validate(jsonData, SchemaType.JsonSchema);

            if (!validationErrors.Any())
            {
                _logger!.LogInformation("The config satisfies the schema requirements.");
                return (true, null);
            }
            else
            {
                return (false, validationErrors);
            }
        }
        catch (Exception e)
        {
            _logger!.LogError($"Failed to validate config against schema due to \n{e.Message}");
            return (false, null);
        }
    }

    /// <summary> 
    /// Validates the given json config. 
    /// </summary> 
    /// <param name="jsonConfig">The JSON config file to validate.</param> 
    public static async Task ValidateJsonConfig(string jsonConfig)
    {
        // deserialize jsonData to runtimeConfig
        if (!RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? config))
        {
            Console.WriteLine("The config is invalid.");
            return;
        }
        

        string? jsonSchema = await GetJsonSchema(config);

        if (string.IsNullOrWhiteSpace(jsonSchema))
        {
            _logger!.LogWarning("The schema file is invalid. Unable to verify provided config against schema.");
            return;
        }

        (bool isValid, ICollection<ValidationError>? validationErrors) = await ValidateJsonConfigWithSchemaAsync(jsonSchema, jsonConfig);
        
        if (!isValid && validationErrors != null)
        {
            _logger!.LogInformation(FormatSchemaValidationErrorMessage(validationErrors));
        }
    }

    /// <summary>
    /// Retrieves the JSON schema for validation from the provided runtime config or the assembly package. 
    /// </summary> 
    /// <param name="runtimeConfig">The runtimeConfig object containing the schema information.</param> 
    /// <returns>The JSON schema as a string, or null if the schema cannot be obtained.</returns> 
    private static async Task<string?> GetJsonSchema(RuntimeConfig runtimeConfig)
    {
        if (!string.IsNullOrWhiteSpace(runtimeConfig.Schema))
        {
            try {
                JsonSchema jsonSchema = await JsonSchema.FromUrlAsync(runtimeConfig.Schema);
                return jsonSchema.ToJson();
            }
            catch (Exception e)
            {
                _logger!.LogError($"Failed to get schema from url: {runtimeConfig.Schema}\n{e}");
            }
        }

        try {
            return GetSchemaFromAssemblyPackage();
        }
        catch (Exception e)
        {
            _logger!.LogError($"Failed to get schema from assembly package\n{e}");
        }

        return null;
    }

    /// <summary> 
    /// Formats a collection of validation errors into a human-readable error message. 
    /// </summary> 
    /// <param name="validationErrors">The collection of validation errors to format.</param> 
    /// <returns>The formatted error message.</returns> 
    public static string FormatSchemaValidationErrorMessage(ICollection<ValidationError> validationErrors)
    {
        return $"> Total schema validation errors: {validationErrors.Count}\n" +
            string.Join("", validationErrors.Select(e => $"> {e} at " +
            $"{e.LineNumber}:{e.LinePosition}\n\n"));
    }

    /// <summary> 
    /// Retrieves the JSON schema from the assembly package. 
    /// </summary>
    /// <returns>The contents of the JSON schema file.</returns> 
    private static string GetSchemaFromAssemblyPackage()
    {
        string assemblyPath = Assembly.GetExecutingAssembly().Location;
        string directoryPath = _fileSystem.Path.GetDirectoryName(assemblyPath)!;
        string jsonPath = _fileSystem.Path.Combine(directoryPath, "dab.draft.schema.json");

        string contents = File.ReadAllText(jsonPath);
        return contents;
    }
}
