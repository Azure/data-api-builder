// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Supported source types as defined by json schema
/// </summary>
public enum EntitySourceType
{
    Table,
    View,
    [EnumMember(Value = "stored-procedure")] StoredProcedure
}
