
namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Modifies a query that returns regular rows to return JSON for Postgres
    /// </summary>
    public class PostgresQueryBuilder: IQueryBuilder {
        public string Build(string inputQuery, bool isList) {
            if (!isList) {
                return $"SELECT row_to_json(q) FROM ({inputQuery}) q";
            }
            return $"SELECT jsonb_agg(row_to_json(q)) FROM ({inputQuery}) q";
        }

    }
}
