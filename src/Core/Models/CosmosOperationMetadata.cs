// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Metadata for the Cosmos engines to understand an operation to undertake
/// </summary>
/// <param name="DatabaseName">Name of the database</param>
/// <param name="ContainerName">Name of the container</param>
/// <param name="OperationType">Type of operation to perform</param>
record CosmosOperationMetadata(string DatabaseName, string ContainerName, EntityActionOperation OperationType);
