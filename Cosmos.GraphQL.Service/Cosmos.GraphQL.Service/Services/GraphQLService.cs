﻿using Cosmos.GraphQL.Service.Models;
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
        private readonly IDocumentExecuter _executor = new DocumentExecuter();

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
        private IMetadataStoreProvider _metadataStoreProvider;

        public GraphQLService(QueryEngine queryEngine, MutationEngine mutationEngine, CosmosClientProvider clientProvider, IMetadataStoreProvider metadataStoreProvider)
        {
            this._queryEngine = queryEngine;
            this._mutationEngine = mutationEngine;
            this._metadataStoreProvider = metadataStoreProvider;
            this._writer = new MyDocumentWriter(this._writerPure);
        }

        public void parseAsync(String data)
        {
            schemaAsString = data;
            _schema = Schema.For(data);
            this._metadataStoreProvider.StoreGraphQLSchema(data);
            this._schema.FieldMiddleware.Use(new InstrumentFieldsMiddleware());
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
            var result = await _executor.ExecuteAsync(options =>
            {
                
                
                string query = (string) request["query"];
                options.Schema = _schema;
                options.Query = query;
                options.EnableMetrics = true;
                // options.Root = new { query = GetString() };

            });
            // return await _writer.WriteToStringAsync(ExecutionResult);
            var responseString = await _writer.WriteToStringAsync(result);
            return responseString;
        }

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

        public void attachQueryResolverToSchema(string queryName)
        {
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

    }
}
