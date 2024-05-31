// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HostOptions
{
    /// <summary>
    /// Dab engine can at maximum handle 187 MB of data in a single response from a source.
    /// Json deserialization of a response into a string has a limit of 197020041 bytes which when converted to MB is 187 MB.
    /// </summary>
    private const int MAX_RESPONSE_LENGTH_DAB_ENGINE_MB = 187;

    [JsonPropertyName("cors")]
    public CorsOptions? Cors { get; init; }

    [JsonPropertyName("authentication")]
    public AuthenticationOptions? Authentication { get; init; }

    [JsonPropertyName("mode")]
    public HostMode Mode { get; init; }

    [JsonPropertyName("max-response-size-mb")]
    public int? MaxResponseSizeMB { get; init; }

    public HostOptions(CorsOptions? Cors, AuthenticationOptions? Authentication, HostMode Mode = HostMode.Production, int? MaxResponseSizeMB = null)
    {
        this.Cors = Cors;
        this.Authentication = Authentication;
        this.Mode = Mode;
        this.MaxResponseSizeMB = MaxResponseSizeMB;

        if (this.MaxResponseSizeMB is not null && (this.MaxResponseSizeMB < 1 || this.MaxResponseSizeMB > MAX_RESPONSE_LENGTH_DAB_ENGINE_MB))
        {
            throw new DataApiBuilderException(
                message: $"{nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} must be greater than 0 and <= 187 MB.",
                statusCode: HttpStatusCode.ServiceUnavailable,
                subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
        }
    }
}
