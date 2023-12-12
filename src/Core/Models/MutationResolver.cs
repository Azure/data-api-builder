// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

public record MutationResolver(string Id, Operation OperationType, string DatabaseName, string ContainerName, string Fields, string Table);
