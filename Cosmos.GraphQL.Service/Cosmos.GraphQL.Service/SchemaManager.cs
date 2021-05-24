using Cosmos.GraphQL.Service.Models;
using GraphQL.Types;
using GraphQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL.SystemTextJson;
using Newtonsoft.Json.Linq;

namespace Cosmos.GraphQL.Service
{
    public class SchemaManager
    {
        private Schema _schema;
        private string schemaAsString;
        protected readonly IDocumentWriter Writer = new DocumentWriter(indent: true);


        public void parseAsync(String data)
        {
            schemaAsString = data;
            this._schema = Schema.For(data);
        }

        public Schema Schema
        {
            get { return _schema; }
        }

        public string GetString()
        {
            return "Hello World!";
        }

        internal async Task<object> ExecuteAsync(string data)
        {
            var executor = new DocumentExecuter();

            JObject jobject = JObject.Parse(data);
            string query = (string)jobject["query"];

            var ExecutionResult = await _schema.ExecuteAsync(Writer, options =>
            {
                options.Schema = _schema;
                options.Query = query;
                options.Root = new { Hello = GetString() };

            });
            return ExecutionResult;
        }
    }
}
