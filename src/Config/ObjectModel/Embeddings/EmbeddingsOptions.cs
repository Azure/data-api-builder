// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

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
    /// Whether the embedding service is enabled. Defaults to true.
    /// When false, the embedding service will not be used.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Flag indicating whether the user provided the enabled setting.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedEnabled { get; init; }

    /// <summary>
    /// The embedding provider type (azure-openai or openai).
    /// Required.
    /// </summary>
    [JsonPropertyName("provider")]
    public EmbeddingProviderType Provider { get; init; }

    /// <summary>
    /// The provider base URL.
    /// Required.
    /// </summary>
    [JsonPropertyName("base-url")]
    public string BaseUrl { get; init; }

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
    /// Endpoint configuration for the embedding service.
    /// </summary>
    [JsonPropertyName("endpoint")]
    public EmbeddingsEndpointOptions? Endpoint { get; init; }

    /// <summary>
    /// Health check configuration for the embedding service.
    /// </summary>
    [JsonPropertyName("health")]
    public EmbeddingsHealthCheckConfig? Health { get; init; }

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

    /// <summary>
    /// Returns true if embedding health check is enabled.
    /// </summary>
    [JsonIgnore]
    public bool IsHealthCheckEnabled => Health?.Enabled ?? false;

    /// <summary>
    /// Returns true if embedding endpoint is enabled.
    /// </summary>
    [JsonIgnore]
    public bool IsEndpointEnabled => Endpoint?.Enabled ?? false;

    /// <summary>
    /// Gets the effective endpoint path.
    /// </summary>
    [JsonIgnore]
    public string EffectiveEndpointPath => Endpoint?.EffectivePath ?? EmbeddingsEndpointOptions.DEFAULT_PATH;

    [JsonConstructor]
    public EmbeddingsOptions(
        EmbeddingProviderType Provider,
        string BaseUrl,
        string ApiKey,
        bool? Enabled = null,
        string? Model = null,
        string? ApiVersion = null,
        int? Dimensions = null,
        int? TimeoutMs = null,
        EmbeddingsEndpointOptions? Endpoint = null,
        EmbeddingsHealthCheckConfig? Health = null)
    {
        this.Provider = Provider;
        this.BaseUrl = BaseUrl;
        this.ApiKey = ApiKey;
        this.Endpoint = Endpoint;
        this.Health = Health;

        if (Enabled.HasValue)
        {
            this.Enabled = Enabled.Value;
            UserProvidedEnabled = true;
        }
        else
        {
            this.Enabled = true; // Default to enabled
        }

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
