// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// The operations supported by the service.
/// </summary>
public enum EntityActionOperation
{
    None,

    // *
    [EnumMember(Value = "*")] All,

    // Common Operations
    Delete, Read,

    // cosmosdb_nosql operations
    Upsert, Create,

    // Sql operations
    Insert, Update, UpdateGraphQL,

    // Additional
    UpsertIncremental, UpdateIncremental,

    // Only valid operation for stored procedures
    Execute
}
