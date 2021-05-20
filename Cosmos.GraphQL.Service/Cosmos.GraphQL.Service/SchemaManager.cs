using Cosmos.GraphQL.Service.Models;
using GraphQL.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service
{
    public class SchemaManager
    {
        private Schema _schema;
        private string schemaData;
        public void parse(String data)
        {
            schemaData = data;
            this._schema = Schema.For(data);
        }

        public Schema Schema
        {
            get { return _schema; }
        }
    }
}
