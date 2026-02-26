// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Health check configuration for embeddings.
/// Validates that the embedding service is responding within threshold and returning expected results.
/// </summary>
public record EmbeddingsHealthCheckConfig : HealthCheckConfig
{
    /// <summary>
    /// Default threshold for embedding health check in milliseconds.
    /// </summary>
    public const int DEFAULT_THRESHOLD_MS = 5000;

    /// <summary>
    /// Default test text used for health check validation.
    /// </summary>
    public const string DEFAULT_TEST_TEXT = "health check";

    /// <summary>
    /// The expected milliseconds the embedding request should complete within to be considered healthy.
    /// If the request takes longer than this value, the health check will be considered unhealthy.
    /// Requests completing at exactly the threshold are considered healthy.
    /// Default: 5000ms (5 seconds)
    /// </summary>
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided a custom threshold.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedThresholdMs { get; init; }

    /// <summary>
    /// The test text to use for health check validation.
    /// This text will be embedded and the result validated.
    /// Default: "health check"
    /// </summary>
    [JsonPropertyName("test-text")]
    public string TestText { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided custom test text.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedTestText { get; init; }

    /// <summary>
    /// The expected number of dimensions in the embedding result.
    /// If specified, the health check will verify the embedding has this many dimensions.
    /// If not specified, dimension validation is skipped.
    /// </summary>
    [JsonPropertyName("expected-dimensions")]
    public int? ExpectedDimensions { get; init; }

    /// <summary>
    /// Flag indicating whether the user provided expected dimensions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedExpectedDimensions { get; init; }

    /// <summary>
    /// Default constructor with default values.
    /// </summary>
    public EmbeddingsHealthCheckConfig() : base()
    {
        ThresholdMs = DEFAULT_THRESHOLD_MS;
        TestText = DEFAULT_TEST_TEXT;
    }

    /// <summary>
    /// Constructor with optional parameters.
    /// </summary>
    [JsonConstructor]
    public EmbeddingsHealthCheckConfig(
        bool? enabled = null,
        int? thresholdMs = null,
        string? testText = null,
        int? expectedDimensions = null) : base(enabled)
    {
        if (thresholdMs is not null)
        {
            ThresholdMs = (int)thresholdMs;
            UserProvidedThresholdMs = true;
        }
        else
        {
            ThresholdMs = DEFAULT_THRESHOLD_MS;
        }

        if (testText is not null)
        {
            TestText = testText;
            UserProvidedTestText = true;
        }
        else
        {
            TestText = DEFAULT_TEST_TEXT;
        }

        if (expectedDimensions is not null)
        {
            ExpectedDimensions = expectedDimensions;
            UserProvidedExpectedDimensions = true;
        }
    }
}
