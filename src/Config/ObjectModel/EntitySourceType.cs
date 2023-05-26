// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public enum EntitySourceType
{
    Table,
    View,
    [EnumMember(Value = "stored-procedure")] StoredProcedure
}
