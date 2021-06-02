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
        public static string GraphQLTestSchema = @" type Query {
                                              myQuery: MyPojo
                                        }


                                        type Mutation {
                                            addPost(
                                                myProp: String!
                                                id : String!
                                            ): MyPojo
                                        }

                                        type MyPojo {
                                            id : String,
                                            myProp : String,
                                            myIntProp : Int,
                                            myBooleanProp : Boolean,
                                            myFloatProp : Float,
                                            anotherPojo : AnotherPojo
                                        }

                                        type AnotherPojo {
                                            anotherProp : String,
                                            anotherIntProp : Int,
                                            person: Person
                                        }

                                        type Person {
                                            firstName: String,
                                            lastName: String,
                                            zipCode: Int
                                        }
                                        ";

        public static string SampleQuery = "{ \"query\":\"{"+ QUERY_NAME +"{ myProp myIntProp myBooleanProp anotherPojo { anotherProp person { lastName } } } }\"} ";

        public static string SampleMutation = "{\"query\":\"mutation addPost {\\r\\n  addPost(\\r\\n    myProp : \\\"myValue345\\\"\\r\\n    id : \\\"myId2\\\"\\r\\n  ) {\\r\\n      myProp\\r\\n  }\\r\\n}\"}";

        public static GraphQLQueryResolver SampleQueryResolver()
        {
            return new GraphQLQueryResolver
            {
                GraphQLQueryName = QUERY_NAME,
                dotNetCodeRequestHandler = "",
                dotNetCodeResponseHandler = "JObject toDoActivity = (JObject)await container.ReadItemAsync<JObject>(\"MyTestItemId2\", new PartitionKey(\"MyTestItemId2\")); toDoActivity",
            };
        }

        public static MutationResolver SampleMutationResolver()
        {
            return new MutationResolver
            {
                graphQLMutationName = MUTATION_NAME,
                dotNetCodeRequestHandler = "dynamic testItem = new { id = \"MyTestItemId2\", myProp = \"it's working2\", status = \"done\" };\r\nawait container.UpsertItemAsync(testItem);",
                dotNetCodeResponseHandler = "JObject toDoActivity = (JObject) await container.ReadItemAsync<JObject>(\"MyTestItemId2\", new PartitionKey(\"MyTestItemId2\"));  toDoActivity"
            };
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
