// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;

namespace Azure.DataApiBuilder.Core.Services
{
    public interface IConnectionProvider<ConnectionT>  where ConnectionT : DbConnection, new()
    {
        ConnectionT Create() => new();
    }
}
