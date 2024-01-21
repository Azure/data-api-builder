// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Options for nested insert operations.
/// </summary>
/// <param name="Enabled"> Indicates whether nested insert operation is enabled.</param>
public class NestedInsertOptions
{
    public bool Enabled;

    public NestedInsertOptions(bool enabled)
    {
        Enabled = enabled;
    }
};

