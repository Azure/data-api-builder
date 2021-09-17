using Cosmos.GraphQL.Service.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service;
using Cosmos.GraphQL.Service.Resolvers;
using Newtonsoft.Json.Linq;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Language;

namespace Cosmos.GraphQL.Services
{
    public class GraphQLService
    {
        private ISchema _schema;
        private string schemaAsString;
        //private readonly IDocumentWriter _writerPure = new DocumentWriter(indent: true);
        //private readonly IDocumentExecuter _executor = new DocumentExecuter();

        //private readonly IDocumentWriter _writer;

        private readonly QueryEngine _queryEngine;
        private readonly MutationEngine _mutationEngine;
        private IMetadataStoreProvider _metadataStoreProvider;

        public GraphQLService(QueryEngine queryEngine, MutationEngine mutationEngine, CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._queryEngine = queryEngine;
            this._mutationEngine = mutationEngine;
            this._metadataStoreProvider = metadataStoreProvider;
            //this._writer = new MyDocumentWriter(this._writerPure);
        }

        public void parseAsync(String data)
        {
            schemaAsString = data;
            ISchema schema = SchemaBuilder.New()
                .AddDocumentFromString(data)
                //.Use<MyMiddleware>()
                .Use((services, next) => new MyMiddleware(next, _queryEngine))
                .Create();
            _schema = schema;
            this._metadataStoreProvider.StoreGraphQLSchema(data);
            //this._schema.FieldMiddleware.Use(new InstrumentFieldsMiddleware());
        }

        public class MyMiddleware
        {
            private readonly FieldDelegate _next;
            private readonly QueryEngine _queryEngine;

            public MyMiddleware(FieldDelegate next, QueryEngine queryEngine)
            {
                _next = next;
                _queryEngine = queryEngine;
            }

            public MyMiddleware(FieldDelegate next)
            {
                _next = next;
            }

            public async Task InvokeAsync(IMiddlewareContext context)
            {
                if (context.Selection.Field.Coordinate.TypeName.Value == "Query")
                {
                    IReadOnlyList<ArgumentNode> arguments = context.FieldSelection.Arguments;
                    IDictionary<string, object> parameters = new Dictionary<string, object>();
                    IEnumerator<ArgumentNode> enumerator = arguments.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        ArgumentNode current = enumerator.Current;
                        parameters.Add(current.Name.Value, current.Value.Value);

                    }

                    if (context.Selection.Type.IsListType())
                    {
                        context.Result = _queryEngine.executeList(context.Selection.Field.Name.Value, parameters);
                    } else
                    {
                        context.Result = _queryEngine.execute(context.Selection.Field.Name.Value, parameters);
                    }
                }

                if (context.Selection.Field.Type.IsLeafType())
                {
                    JsonDocument result = context.Parent<JsonDocument>();
                    context.Result = result.RootElement.GetProperty(context.Selection.Field.Name.Value).ToString();
                }
              
                await _next(context);
            }

        }

        public ISchema Schema
        {
            get { return _schema; }
        }

        internal async Task<string> ExecuteAsync(String requestBody)
        {
            if (this.Schema == null)
            {
                return "{\"error\": \"Schema must be defined first\" }";
            }
            JsonDocument req = JsonDocument.Parse(requestBody);
            IQueryRequest queryRequest = QueryRequestBuilder.New()
                .SetQuery(req.RootElement.GetProperty("query").GetString())
                .Create();

            //var request = requestBody.ToInputs();
            IRequestExecutor executor = Schema.MakeExecutable();
            IExecutionResult result =
                await executor.ExecuteAsync(queryRequest);

            /*
            var result = await _executor.ExecuteAsync(options =>
            {
                
                
                string query = (string) request["query"];
                options.Schema = _schema;
                options.Query = query;
                options.EnableMetrics = true;
                // options.Root = new { query = GetString() };

            });
            // return await _writer.WriteToStringAsync(ExecutionResult);
            */
            //var responseString = await _writer.WriteToStringAsync(result);
            // return responseString;

            return result.ToJson();
        }

        /*
        class MyFieldResolver : IFieldResolver
        {
            JsonDocument result; 
            public object Resolve(IResolveFieldContext context)
            {
                // TODO: add support for error case

                var jsonDoc = context.Source as JsonDocument;
                result = jsonDoc;

                return getResolvedValue(result.RootElement, context.FieldDefinition.ResolvedType.Name, context.Path);
            }

            object getResolvedValue(JsonElement rootElement, String typeName, IEnumerable<object> propertyPath)
            {
                JsonElement value = new JsonElement();
                bool success = false;
                for ( int i = 1; i < propertyPath.Count(); i++)
                {
                    var currentPathElement = propertyPath.ElementAt(i);
                    if (currentPathElement is not string)
                    {
                        continue;
                    }
                    success = rootElement.TryGetProperty((string)currentPathElement, out value);
                    if (success)
                    {
                        rootElement = value;
                    } else
                    {
                        break;
                    }

                }
                //bool success = rootElement.TryGetProperty(definitionName, out JsonElement value);
                if (!success)
                {
                    return null;
                }
                switch (typeName)
                {
                    case "String":
                        return value.GetString();
                    case "Int":
                        return value.GetInt32();
                    case "Float":
                        return value.GetDouble();
                    case "Boolean":
                        return value.GetBoolean();
                    //case "ID":
                    //    return rootElement.GetProperty(definitionName).GetString();
                    default:
                        throw new InvalidDataException();

                }
            }

        }


        class InstrumentFieldsMiddleware : IFieldMiddleware
        {
            MyFieldResolver fieldResolver = new MyFieldResolver();
            public Task<object> Resolve(
                IResolveFieldContext context,
                FieldMiddlewareDelegate next)
            {
                // TODO: add support for nesting
                // TODO: add support for non string later
                // TODO: add support for error case

                if (context.FieldDefinition.ResolvedType.IsLeafType())
                     //|| context.FieldDefinition.ResolvedType is ListGraphType)
                {
                    if (IsIntrospectionPath(context.Path))
                    {
                        return next(context);
                    }
                    context.FieldDefinition.Resolver = fieldResolver;
                    return Task.FromResult<object>(context.FieldDefinition.Resolver.Resolve(context));
                }

                return next(context);
            }

        }
        */

        public void attachQueryResolverToSchema(string queryName)
        {
            /*
            if (_queryEngine.isListQuery(queryName))
            {
                this._schema.Query.GetField(queryName).Resolver =
               new FuncFieldResolver<object, IEnumerable<JsonDocument>>(context =>
               {
                   return _queryEngine.executeList(queryName, context.Arguments);
               });
            }
            else
            {
                this._schema.Query.GetField(queryName).Resolver =
                new FuncFieldResolver<object, JsonDocument>(context =>
                {
                    return _queryEngine.execute(queryName, context.Arguments);
                });
            }
            */
        }

        public void attachMutationResolverToSchema(string mutationName)
        {
            /*
            this._schema.Mutation.GetField(mutationName).Resolver = new AsyncFieldResolver<object, JsonDocument>(context =>
            {
                return this._mutationEngine.execute(mutationName, context.Arguments);
            });
            */
        }

        private static bool IsIntrospectionPath(IEnumerable<object> path)
        {
            if (path.Any())
            {
                var firstPath = path.First() as string;
                if (firstPath.StartsWith("__", StringComparison.InvariantCulture))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
