// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models;

/// <summary>
/// Represents the database query built from a query structure.
/// Contains all query metadata need to create a cache key.
/// </summary>
public class DatabaseQueryMetadata
{
    public string QueryText { get; }
    public string DataSource { get; }
    public Dictionary<string, DbConnectionParam> QueryParameters { get; }

    /// <summary>
    /// Creates a "Data Transfer Object" (DTO) used for provided query metadata to dependent services.
    /// </summary>
    /// <param name="queryText">Raw query text built from a query structure object.</param>
    /// <param name="dataSource">Name of the data source where the query will execute.</param>
    /// <param name="queryParameters">Dictonary of query parameter names and values.</param>
    public DatabaseQueryMetadata(string queryText, string dataSource, Dictionary<string, DbConnectionParam> queryParameters)
    {
        QueryText = queryText;
        DataSource = dataSource;
        QueryParameters = queryParameters;
    }
}
