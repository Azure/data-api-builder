// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;

namespace Azure.DataApiBuilder.Core.Services;

public interface IConnectionProvider<TConnection>  where TConnection : DbConnection, new()
{
    DbConnection Create() => new TConnection();
}
