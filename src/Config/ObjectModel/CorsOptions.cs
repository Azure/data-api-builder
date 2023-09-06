// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Configuration related to Cross Origin Resource Sharing (CORS).
/// </summary>
/// <param name="Origins">List of allowed origins.</param>
/// <param name="AllowCredentials">
/// Whether to set Access-Control-Allow-Credentials CORS header.</param>
public record CorsOptions
{
    public string[] Origins;

    public bool AllowCredentials;

    [JsonConstructor]
    public CorsOptions(string[]? Origins, bool AllowCredentials = false)
    {
        this.Origins = Origins is null ? new string[] { } : Origins.ToArray();
        this.AllowCredentials = AllowCredentials;
    }
}
