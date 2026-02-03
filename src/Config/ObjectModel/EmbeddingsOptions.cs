// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring the embedding service.
/// Used for text embedding/vectorization with OpenAI or Azure OpenAI providers.
/// </summary>
public record EmbeddingsOptions
{
    /// <summary>
    /// Default timeout in milliseconds for embedding requests.
    /// </summary>
    public const int DEFAULT_TIMEOUT_MS = 30000;

    /// <summary>
    /// Default API version for Azure OpenAI.
    /// </summary>
    public const string DEFAULT_AZURE_API_VERSION = "2024-02-01";

    /// <summary>
    /// Default model for OpenAI embeddings.
    /// </summary>
    public const string DEFAULT_OPENAI_MODEL = "text-embedding-3-small";

    /// <summary>
    /// The embedding provider type (azure-openai or openai).
    /// Required.
    /// </summary>
    [JsonPropertyName("provider")]
    public EmbeddingProviderType Provider { get; init; }

    /// <summary>
    /// The provider base URL endpoint.
    /// Required.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; }

    /// <summary>
    /// The API key for authentication.
    /// Required.
    /// </summary>
    [JsonPropertyName("api-key")]
    public string ApiKey { get; init; }

    /// <summary>
    /// The model or deployment name.
    /// For Azure OpenAI, this is the deployment name.
    /// For OpenAI, this is the model name (defaults to text-embedding-3-small if not specified).
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>
    /// Azure API version. Only used for Azure OpenAI provider.
    /// Defaults to 2024-02-01.
    /// </summary>
    [JsonPropertyName("api-version")]
    public string? ApiVersion { get; init; }

    /// <summary>
    /// Output vector dimensions. Optional, uses model default if not specified.
    /// </summary>
    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; init; }

    /// <summary>
    /// Request timeout in milliseconds. Defaults to 30000 (30 seconds).
    /// </summary>
    [JsonPropertyName("timeout-ms")]
    public int? TimeoutMs { get; init; }

    /// <summary>
    /// Flag which informs whether the user provided a custom timeout value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(TimeoutMs))]
    public bool UserProvidedTimeoutMs { get; init; }

    /// <summary>
    /// Flag which informs whether the user provided a custom API version.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ApiVersion))]
    public bool UserProvidedApiVersion { get; init; }

    /// <summary>
    /// Flag which informs whether the user provided custom dimensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Dimensions))]
    public bool UserProvidedDimensions { get; init; }

    /// <summary>
    /// Flag which informs whether the user provided a custom model.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Model))]
    public bool UserProvidedModel { get; init; }

    /// <summary>
    /// Gets the effective timeout in milliseconds, using default if not specified.
    /// </summary>
    [JsonIgnore]
    public int EffectiveTimeoutMs => TimeoutMs ?? DEFAULT_TIMEOUT_MS;

    /// <summary>
    /// Gets the effective API version for Azure OpenAI, using default if not specified.
    /// </summary>
    [JsonIgnore]
    public string EffectiveApiVersion => ApiVersion ?? DEFAULT_AZURE_API_VERSION;

    /// <summary>
    /// Gets the effective model name, using default for OpenAI if not specified.
    /// For Azure OpenAI, model is required (no default).
    /// </summary>
    [JsonIgnore]
    public string? EffectiveModel => Model ?? (Provider == EmbeddingProviderType.OpenAI ? DEFAULT_OPENAI_MODEL : null);

    [JsonConstructor]
    public EmbeddingsOptions(
        EmbeddingProviderType Provider,
        string Endpoint,
        string ApiKey,
        string? Model = null,
        string? ApiVersion = null,
        int? Dimensions = null,
        int? TimeoutMs = null)
    {
        this.Provider = Provider;
        this.Endpoint = Endpoint;
        this.ApiKey = ApiKey;

        if (Model is not null)
        {
            this.Model = Model;
            UserProvidedModel = true;
        }

        if (ApiVersion is not null)
        {
            this.ApiVersion = ApiVersion;
            UserProvidedApiVersion = true;
        }

        if (Dimensions is not null)
        {
            this.Dimensions = Dimensions;
            UserProvidedDimensions = true;
        }

        if (TimeoutMs is not null)
        {
            this.TimeoutMs = TimeoutMs;
            UserProvidedTimeoutMs = true;
        }
    }
}
