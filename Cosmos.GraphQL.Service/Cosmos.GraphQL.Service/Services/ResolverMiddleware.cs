using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Resolvers;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

                context.Result = await _mutationEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters);
            }

            if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
            {
                IDictionary<string, object> parameters = GetParametersFromContext(context);
                string queryId = context.Selection.Field.Name.Value;
                bool isContinuationQuery = IsContinuationQuery(parameters);

                if (context.Selection.Type.IsListType())
                {
                    context.Result = await _queryEngine.ExecuteListAsync(queryId, parameters, isContinuationQuery);
                }
                else
                {
                    context.Result = await _queryEngine.ExecuteAsync(context.Selection.Field.Name.Value, parameters, isContinuationQuery);
                }
            }
            else
            {

                if (IsInnerObject(context))
                {
                    JsonDocument result = context.Parent<JsonDocument>();
                    //if (result == default && context.Result != default)
                    //{
                    //    result = (JsonDocument)context.Result;
                    //}

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
                }else if (context.Selection.Field.Type.IsListType())
                {
                    JsonDocument result = context.Parent<JsonDocument>();
                    JsonElement jsonElement;
                    bool hasProperty =
                        result.RootElement.TryGetProperty(context.Selection.Field.Name.Value, out jsonElement);
                    if (result != null && hasProperty)
                    {
                        //TODO: Ugly logic warning!!!! System.Text.Json seem to to have very limited capabilities
                        IEnumerable<JObject> resultArray = JsonConvert.DeserializeObject<JObject[]>(jsonElement.ToString());
                        List<JsonDocument> resultList = new();
                        IEnumerator<JObject> enumerator = resultArray.GetEnumerator();
                        while (enumerator.MoveNext())
                        {
                            resultList.Add(JsonDocument.Parse(enumerator.Current.ToString()));
                        }
                        context.Result = resultList;
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
            }

            await _next(context);
        }

        private static bool IsContinuationQuery(IDictionary<string, object> parameters)
        {
           if (parameters.ContainsKey("after"))
            {
                return true;
            }
            return false;
        }

        private static bool IsInnerObject(IMiddlewareContext context)
        {
            return context.Selection.Field.Type.IsObjectType() && context.Parent<JsonDocument>() != default;
        }

        private IDictionary<string, object> GetParametersFromContext(IMiddlewareContext context)
        {
            IReadOnlyList<ArgumentNode> arguments = context.Selection.SyntaxNode.Arguments;
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
