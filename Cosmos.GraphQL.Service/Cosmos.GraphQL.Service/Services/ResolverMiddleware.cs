using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;

namespace Cosmos.GraphQL.Services
{
    /// <summary>
    /// The resolver middleware that is used by the schema executor to resolve
    /// the queries and mutations
    /// </summary>
    public class ResolverMiddleware
    {
        private readonly FieldDelegate _next;
        private readonly IQueryEngine _queryEngine;
        private readonly IMutationEngine _mutationEngine;

        public ResolverMiddleware(FieldDelegate next, IQueryEngine queryEngine, IMutationEngine mutationEngine)
        {
            _next = next;
            _queryEngine = queryEngine;
            _mutationEngine = mutationEngine;
        }

        public ResolverMiddleware(FieldDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(IMiddlewareContext context)
        {
            if (context.Selection.Field.Coordinate.TypeName.Value == "Mutation")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                context.Result = _mutationEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters).Result;
            }

            if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);

                if (context.Selection.Type.IsListType())
                {
                    context.Result = _queryEngine.ExecuteListAsync(context.Selection.Field.Name.Value, parameters);
                }
                else
                {
                    context.Result = _queryEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters);
                }
            }

            if (isInnerObject(context))
            {
                JsonDocument result = context.Parent<JsonDocument>();

                JsonElement jsonElement;
                bool hasProperty =
                    result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
                if (result != null && hasProperty)
                {
                    //TODO: Try to avoid additional deserialization/serialization here.
                    context.Result = JsonDocument.Parse(jsonElement.ToString());
                }
                else
                {
                    context.Result = null;
                }
            }

            if (context.Selection.Field.Type.IsLeafType())
            {
                JsonDocument result = context.Parent<JsonDocument>();
                JsonElement jsonElement;
                bool hasProperty =
                    result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
                if (result != null && hasProperty)
                {
                    context.Result = jsonElement.ToString();
                }
                else
                {
                    context.Result = null;
                }
            }

            await _next(context);
        }

        private bool isInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        private IDictionary<string, object> GetParametersFromContext(IMiddlewareContext context)
        {
            IReadOnlyList<ArgumentNode> arguments = context.FieldSelection.Arguments;
            IDictionary<string, object> parameters = new Dictionary<string, object>();
            IEnumerator<ArgumentNode> enumerator = arguments.GetEnumerator();
            while (enumerator.MoveNext())
            {
                ArgumentNode current = enumerator.Current;
                parameters.Add(current.Name.Value, current.Value.Value);
            }

            return parameters;
        }
    }
}
