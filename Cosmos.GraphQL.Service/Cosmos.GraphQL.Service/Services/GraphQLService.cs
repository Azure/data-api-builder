using Cosmos.GraphQL.Service.Models;
using GraphQL.Types;
using GraphQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL.SystemTextJson;
using GraphQL.Resolvers;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service
{
    public class GraphQLService
    {
        private Schema _schema;
        private string schemaAsString;
        private readonly IDocumentWriter _writer = new DocumentWriter(indent: true);
        private readonly QueryEngine _queryEngine;

        public GraphQLService(QueryEngine queryEngine)
        {
            this._queryEngine = queryEngine;
        }

        public void parseAsync(String data)
        {
            schemaAsString = data;
            this._schema = Schema.For(data);
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

        public void attachQueryResolverToSchema(string queryName)
        {
            this._schema.Query.GetField(queryName).Resolver =
              new AsyncFieldResolver<object, string>(async context => { return  await _queryEngine.execute(queryName, new Dictionary<string, string>()); }); 
        }

        public void attachMutationResolverToSchema(string mutationName)
        {
            this._schema.Mutation.GetField(mutationName).Resolver = new FuncFieldResolver<object, string>(context => { return "Hello world"; });
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
