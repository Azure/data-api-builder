// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the configuration options for embedding provider.
/// </summary>
public record EmbeddingProviderOptions
{
    /// <summary>
    /// Provider type. Currently supported: "azure-openai"
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// Azure OpenAI endpoint.
    /// </summary>
    public string? Endpoint { get; init; }

    /// <summary>
    /// Azure OpenAI API key.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Embedding model deployment name.
    /// Example: "text-embedding-3-small"
    /// </summary>
    public string? Model { get; init; }

    [JsonConstructor]
    public EmbeddingProviderOptions(
        string? type = null,
        string? endpoint = null,
        string? apiKey = null,
        string? model = null)
    {
        if (type is not null)
        {
            Type = type;
            UserProvidedType = true;
        }

        if (endpoint is not null)
        {
            Endpoint = endpoint;
            UserProvidedEndpoint = true;
        }

        if (apiKey is not null)
        {
            ApiKey = apiKey;
            UserProvidedApiKey = true;
        }

        if (model is not null)
        {
            Model = model;
            UserProvidedModel = true;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write type
    /// property and value to the runtime config file.
    /// When user doesn't provide the type property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Type))]
    public bool UserProvidedType { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write endpoint
    /// property and value to the runtime config file.
    /// When user doesn't provide the endpoint property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Endpoint))]
    public bool UserProvidedEndpoint { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write api-key
    /// property and value to the runtime config file.
    /// When user doesn't provide the api-key property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ApiKey))]
    public bool UserProvidedApiKey { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write model
    /// property and value to the runtime config file.
    /// When user doesn't provide the model property/value, which signals DAB to not write anything,
    /// the DAB CLI should not write the current value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Model))]
    public bool UserProvidedModel { get; init; } = false;
}
