
namespace Cosmos.GraphQL.Service.Resolvers
{
    // <summary>
    // Interface for building of the final query string.
    // </summary>
    public interface IQueryBuilder
    {
        // <summary>
        // Modifies the inputQuery in such a way that it returns the results as
        // a JSON string.
        // </summary>
        public string Build(string inputQuery, bool isList);
    }
}

