// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Exceptions;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record HostOptions
{
    /// <summary>
    /// Dab engine can at maximum handle 158 MB of data in a single response from a source.
    /// Json deserialization of a response into a string has a limit of 166,666,666 bytes which when converted to MB is 158 MB.
    /// ref: enforcing code:
    /// .net8: https://github.com/dotnet/runtime/blob/v6.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Writer/JsonWriterHelper.cs#L80
    /// .net6: https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Text.Json/src/System/Text/Json/Writer/JsonWriterHelper.cs#75
    /// ref: Json constant: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Text.Json/src/System/Text/Json/JsonConstants.cs#L80
    /// </summary>
    public const int MAX_RESPONSE_LENGTH_DAB_ENGINE_MB = 158;

    /// <summary>
    /// Dab engine default response length. As of now this is same as max response length.
    /// </summary>
    public const int DEFAULT_RESPONSE_LENGTH_DAB_ENGINE_MB = 158;

    [JsonPropertyName("cors")]
    public CorsOptions? Cors { get; init; }

    [JsonPropertyName("authentication")]
    public AuthenticationOptions? Authentication { get; init; }

    [JsonPropertyName("mode")]
    public HostMode Mode { get; init; }

    [JsonPropertyName("max-response-size-mb")]
    public int? MaxResponseSizeMB { get; init; } = null;

    public HostOptions(CorsOptions? Cors, AuthenticationOptions? Authentication, HostMode Mode = HostMode.Production, int? MaxResponseSizeMB = null)
    {
        this.Cors = Cors;
        this.Authentication = Authentication;
        this.Mode = Mode;
        this.MaxResponseSizeMB = MaxResponseSizeMB;

        if (this.MaxResponseSizeMB is not null)
        {
            this.MaxResponseSizeMB = this.MaxResponseSizeMB == -1 ? MAX_RESPONSE_LENGTH_DAB_ENGINE_MB : (int)this.MaxResponseSizeMB;
            if (this.MaxResponseSizeMB < 1 || this.MaxResponseSizeMB > MAX_RESPONSE_LENGTH_DAB_ENGINE_MB)
            {
                throw new DataApiBuilderException(
                    message: $"{nameof(RuntimeConfig.Runtime.Host.MaxResponseSizeMB)} cannot be 0, exceed {MAX_RESPONSE_LENGTH_DAB_ENGINE_MB}MB or be less than -1",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ConfigValidationError);
            }

            UserProvidedMaxResponseSizeMB = true;
        }
        else
        {
            this.MaxResponseSizeMB = DEFAULT_RESPONSE_LENGTH_DAB_ENGINE_MB;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write MaxResponseSizeMB.
    /// property and value to the runtime config file.
    /// When user doesn't provide the MaxResponseSizeMB property/value or provides a null value, which signals DAB to use the default,
    /// the DAB CLI will not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, MaxResponseSizeMB
    /// property/value specified would be interpreted by DAB as "user explicitly set MaxResponseSizeMB.
    /// UserProvidedMaxResponseSizeMB is true only when a user provides a non-null value
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxResponseSizeMB))]
    public bool UserProvidedMaxResponseSizeMB { get; init; } = false;
}
