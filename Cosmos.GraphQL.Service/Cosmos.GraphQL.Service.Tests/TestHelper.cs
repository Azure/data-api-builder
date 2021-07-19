using Cosmos.GraphQL.Service.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    class TestHelper
    {
        public static readonly string QUERY_NAME = "myQuery";
        public static readonly string MUTATION_NAME = "addPost";
        public static string GraphQLTestSchema = @"
type Query {
  myQuery: MyPojo
}

type MyPojo {
    myProp : String
    id : String
}

type Mutation {
    addPost(
        myProp: String!
        id : String!
    ): MyPojo
}";
                                  

        public static string SampleQuery = "{ \"query\":\"{\r\nmyQuery {\r\n    myProp\r\n  }\r\n}\" } ";

        public static string SampleMutation = "{\"query\":\"mutation addPost {\r\n  addPost(\r\n    myProp : \"myValueBM\"\r\n    id : \"myIdBM\"\r\n  ) {\r\n      myProp\r\n  }\r\n}\"}";

        public static GraphQLQueryResolver SampleQueryResolver()
        {
            var raw =
                "{\r\n    \"id\" : \"myQuery\",\r\n    \"databaseName\": \"testDB\",\r\n    \"containerName\": \"testContainer\",\r\n    \"parametrizedQuery\": \"SELECT * FROM r\"\r\n}";

            return JsonConvert.DeserializeObject<GraphQLQueryResolver>(raw);
        }

        public static MutationResolver SampleMutationResolver()
        {
            var raw =
                "{\r\n    \"id\": \"addPost\",\r\n    \"databaseName\": \"testDB\",\r\n    \"containerName\": \"testContainer\",\r\n    \"operationType\": \"UPSERT\"\r\n}\r\n";
            return JsonConvert.DeserializeObject<MutationResolver>(raw);
        } 

        public static object GetItem(string id)
        {
            return new
            {
                id = id,
                myProp = "a value",
                myIntProp = 4,
                myBooleanProp = true,
                anotherPojo = new
                {
                    anotherProp = "myname",
                    anotherIntProp = 55,
                    person = new
                    {
                        firstName = "A Person",
                        lastName = "the last name",
                        zipCode = 784298
                    }
                }
            };
        }
    }
}
