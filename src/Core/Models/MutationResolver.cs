// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

public record MutationResolver(string Id, Operation HttpMethod, string DatabaseName, string ContainerName, string Fields, string Table);
