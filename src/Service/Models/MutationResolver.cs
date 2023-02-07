// **************************************
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// @file: MutationResolver.cs
// **************************************

namespace Azure.DataApiBuilder.Service.Models
{
    public record MutationResolver(string Id, Operation OperationType, string DatabaseName, string ContainerName, string Fields, string Table);
}
