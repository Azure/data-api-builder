// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public enum EntityCacheLevel
{
    [EnumMember(Value = "L1")]
    L1,
    [EnumMember(Value = "L1L2")]
    L1L2
}
