// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RestRuntimeOptions(bool Enabled = true, string Path = RestRuntimeOptions.DEFAULT_PATH)
{
    public const string DEFAULT_PATH = "/api";
};
