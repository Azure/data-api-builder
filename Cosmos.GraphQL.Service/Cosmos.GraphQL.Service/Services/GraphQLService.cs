using Cosmos.GraphQL.Service.Models;
using GraphQL.Types;
using GraphQL;
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
using GraphQL.Instrumentation;
using GraphQL.SystemTextJson;
using GraphQL.Resolvers;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Services
{
    public class GraphQLService
    {
        private Schema _schema;
        private string schemaAsString;
        private readonly IDocumentWriter _writerPure = new DocumentWriter(indent: true);

        // this is just for debugging. 
        // TODO remove and replace with DocumentWriter
        class MyDocumentWriter : IDocumentWriter {
            private IDocumentWriter internalWriter;

            public MyDocumentWriter(IDocumentWriter internalWriter)
            {
                this.internalWriter = internalWriter;
            }

            public Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken = new CancellationToken())
            {
                Console.WriteLine(value);
                return internalWriter.WriteAsync(stream, value, cancellationToken);
            }
        }

        private readonly IDocumentWriter _writer;

        private readonly QueryEngine _queryEngine;
        private readonly MutationEngine _mutationEngine;

        public GraphQLService(QueryEngine queryEngine, MutationEngine mutationEngine, CosmosClientProvider clientProvider)
        {
            this._queryEngine = queryEngine;
            this._mutationEngine = mutationEngine;
            this._writer = new MyDocumentWriter(this._writerPure);
        }

        public void parseAsync(String data)
        {
            schemaAsString = data;
            this._schema = Schema.For(data);
            this._schema.FieldMiddleware.Use(new InstrumentFieldsMiddleware());
            //attachQueryResolverToSchema("hello");
        }

        public Schema Schema
        {
            get { return _schema; }
        }

        public string GetString()
        {
            return "Hello World!";
        }

        internal async Task<string> ExecuteAsync(String requestBody)
        {
            if (this.Schema == null)
            {
                return "{\"error\": \"Schema must be defined first\" }";
            }
            var request = requestBody.ToInputs();
            var ExecutionResult = await _schema.ExecuteAsync(_writer, options =>
            {
                string query = (string) request["query"];
                options.Schema = _schema;
                options.Query = query;
                // options.Root = new { query = GetString() };

            });
            // return await _writer.WriteToStringAsync(ExecutionResult);
            return ExecutionResult;
        }

        class MyFieldResolver : IFieldResolver
        {
            JsonDocument result; 
            public object Resolve(IResolveFieldContext context)
            {
                // TODO: add support for nesting
                // TODO: add support for non string later
                // TODO: add support for error case

                if (result == null || context.Path.Count() <= 2)
                {
                    var jsonDoc = context.Source as JsonDocument;
                    if (jsonDoc != null)
                    {
                        result = jsonDoc;
                    }
                    else
                    {
                        result = (((JsonDocument) ((Task<object>) (context.Source)).Result));
                    }
                }
                return getResolvedValue(result.RootElement, context.FieldDefinition.ResolvedType.Name, context.Path);

            }

            object getResolvedValue(JsonElement rootElement, String typeName, IEnumerable<object> propertyPath)
            {
                JsonElement value = new JsonElement();
                bool success = false;
                for ( int i = 1; i < propertyPath.Count(); i++)
                {
                    success = rootElement.TryGetProperty((string)propertyPath.ElementAt(i), out value);
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
                {
                    if (IsIntrospectionPath(context.Path))
                    {
                        // We dont want to resolve any fields for the introspection query and let the library take care of it
                        return next(context);
                    }
                    context.FieldDefinition.Resolver = fieldResolver;
                    return Task.FromResult<object>(context.FieldDefinition.Resolver.Resolve(context));
                }

                return next(context);
            }
        }

        public void attachQueryResolverToSchema(string queryName)
        {
            this._schema.Query.GetField(queryName).Resolver = 
              new AsyncFieldResolver<object, JsonDocument>(async context => { return  await _queryEngine.execute(queryName, new Dictionary<string, string>()); });
        }

        public void attachMutationResolverToSchema(string mutationName)
        {
            this._schema.Mutation.GetField(mutationName).Resolver = new AsyncFieldResolver<object, JsonDocument>(context =>
            {
                return this._mutationEngine.execute(mutationName, context.Arguments);
            });
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

        public class GenericQuery : ObjectGraphType<object>
        {
            public GenericQuery(string queryName)
            {
                this.Name = "Query";
                this.Field<StringGraphType>(queryName,
                    resolve:  c => GetString());
            }

            string GetString()
            {
                return "Hello World!";
            }
        }
    }
}
