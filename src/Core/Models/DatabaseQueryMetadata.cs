// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Models
{
    internal class DatabaseQueryMetadata
    {
        public string QueryText { get; set; }
        public string DataSource {  get; set; }
        public Dictionary<string, DbConnectionParam> QueryParameters { get; set; }
        public DatabaseQueryMetadata(string queryText, string dataSource, Dictionary<string, DbConnectionParam> queryParameters)
        {
            QueryText = queryText;
            DataSource = dataSource;
            QueryParameters = queryParameters;
        }
    }
}
