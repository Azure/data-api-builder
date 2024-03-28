// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class that holds the options for all multiple mutation operations.
/// </summary>
/// <param name="MultipleCreateOptions">Options for multiple create operation.</param>
public class MultipleMutationOptions
{
    // Options for multiple create operation.
    public MultipleCreateOptions? MultipleCreateOptions;

    public MultipleMutationOptions(MultipleCreateOptions? multipleCreateOptions = null)
    {
        MultipleCreateOptions = multipleCreateOptions;
    }

}
