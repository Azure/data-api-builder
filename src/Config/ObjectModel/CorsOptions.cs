// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record CorsOptions(string[] Origins, bool AllowCredentials = false);
