// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Configuration related to Cross Origin Resource Sharing (CORS).
/// </summary>
/// <param name="Origins">List of allowed origins.</param>
/// <param name="AllowCredentials">
/// Whether to set Access-Control-Allow-Credentials CORS header.</param>
public record CorsOptions(string[] Origins, bool AllowCredentials = false);
