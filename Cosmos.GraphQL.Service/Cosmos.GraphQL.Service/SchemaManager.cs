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
        private readonly IDocumentWriter _writer = new DocumentWriter(indent: true);

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

        internal async Task<object> ExecuteAsync(String requestBody)
        {
            var request = requestBody.ToInputs();
            var ExecutionResult = await _schema.ExecuteAsync(_writer, options =>
            {
                options.Schema = _schema;
                options.Query = (string)request["query"];
                options.Root = new { Hello = GetString() };

            });
            // return await _writer.WriteToStringAsync(ExecutionResult);
            return ExecutionResult;
        }
    }
}
