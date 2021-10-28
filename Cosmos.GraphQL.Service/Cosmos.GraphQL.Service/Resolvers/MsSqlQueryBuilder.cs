
namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MSSQL.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string X_FORJSONSUFFIX = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string X_WITHOUTARRAYWRAPPERSUFFIX = "WITHOUT_ARRAY_WRAPPER";

        public string Build(string inputQuery, bool isList)
        {
            string queryText = inputQuery + X_FORJSONSUFFIX;
            if (!isList)
            {
                queryText += "," + X_WITHOUTARRAYWRAPPERSUFFIX;
            }

            return queryText;
        }

    }
}
