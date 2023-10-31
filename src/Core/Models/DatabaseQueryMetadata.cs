// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

public class DatabaseQueryMetadata
{
    public string QueryText { get; }
    public string DataSource { get; }
    public Dictionary<string, DbConnectionParam> QueryParameters { get; }

    public DatabaseQueryMetadata(string queryText, string dataSource, Dictionary<string, DbConnectionParam> queryParameters)
    {
        QueryText = queryText;
        DataSource = dataSource;
        QueryParameters = queryParameters;
    }
}
