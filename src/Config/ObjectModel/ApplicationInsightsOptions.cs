// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record ApplicationInsightsOptions(bool Enabled = false, string? ConnectionString = null)
{ }
