// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Options for nested create operations.
/// </summary>
/// <param name="Enabled"> Indicates whether nested create operation is enabled.</param>
public class NestedCreateOptions
{
    /// <summary>
    /// Indicates whether nested create operation is enabled.
    /// </summary>
    public bool Enabled;

    public NestedCreateOptions(bool enabled)
    {
        Enabled = enabled;
    }
};

