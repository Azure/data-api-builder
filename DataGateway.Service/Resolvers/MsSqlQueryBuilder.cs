
namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MSSQL.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string X_FOR_JSON_SUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string X_WITHOUT_ARRAY_WRAPPER_SUFFIX = "WITHOUT_ARRAY_WRAPPER";

        public string Build(string inputQuery, bool isList)
        {
            string queryText = inputQuery + X_FOR_JSON_SUFFIX;
            if (!isList)
            {
                queryText += "," + X_WITHOUT_ARRAY_WRAPPER_SUFFIX;
            }

            return queryText;
        }

    }
}
