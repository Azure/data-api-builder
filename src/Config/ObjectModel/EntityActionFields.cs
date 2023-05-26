// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityActionFields(HashSet<string> Exclude, HashSet<string>? Include = null);
