// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityActionFields(
    // Exclude cannot be null, it is initialized with an empty set - no field is excluded.
    HashSet<string> Exclude,

    // Include being null indicates that it was not specified in the config.
    // This is used later (in authorization resolver) as an indicator that
    // Include resolves to all fields present in the config.
    // And so, unlike Exclude, we don't initialize it with an empty set when null.
    HashSet<string>? Include = null);
