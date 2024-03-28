// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Options for multiple create operations.
/// </summary>
/// <param name="Enabled"> Indicates whether multiple create operation is enabled.</param>
public class MultipleCreateOptions
{
    /// <summary>
    /// Indicates whether multiple create operation is enabled.
    /// </summary>
    public bool Enabled;

    public MultipleCreateOptions(bool enabled)
    {
        Enabled = enabled;
    }
};

