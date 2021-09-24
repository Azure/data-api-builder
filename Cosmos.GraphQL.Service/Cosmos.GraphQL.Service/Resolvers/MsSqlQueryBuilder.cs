
namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for MSSQL.
    /// </summary>
    public class MsSqlQueryBuilder : IQueryBuilder
    {
        private const string x_ForJsonSuffix = " FOR JSON PATH, INCLUDE_NULL_VALUES";
        private const string x_WithoutArrayWrapperSuffix = "WITHOUT_ARRAY_WRAPPER";

        public string Build(string inputQuery, bool isList)
        {
            string queryText = inputQuery + x_ForJsonSuffix;
            if (!isList)
            {
                queryText += "," + x_WithoutArrayWrapperSuffix;
            }
            return queryText;
        }

    }
}
