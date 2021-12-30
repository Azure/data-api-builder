using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Used to store shared hard coded expected results for pagination queries between the
    /// MsSql and Postgres tests
    /// </summary>
    [TestClass]
    public abstract class GraphQLPaginationTestBase : SqlTestBase
    {
        protected Dictionary<string, string> ExpectedJsonResultsPerTest { get; } = new()
        {
            ["RequestFullConnection"] = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publisher"": {
                    ""name"": ""Big Company""
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher"": {
                    ""name"": ""Small Town Publisher""
                  }
                }
              ],
              ""endCursor"": ""eyJpZCI6M30="",
              ""hasNextPage"": true
            }",
            ["RequestNoParamFullConnection"] = @"{
              ""items"": [
                {
                  ""id"": 1,
                  ""title"": ""Awesome book""
                },
                {
                  ""id"": 2,
                  ""title"": ""Also Awesome book""
                },
                {
                  ""id"": 3,
                  ""title"": ""Great wall of china explained""
                },
                {
                  ""id"": 4,
                  ""title"": ""US history in a nutshell""
                }
              ],
              ""endCursor"": ""eyJpZCI6NH0="",
              ""hasNextPage"": false
            }",
            ["RequestItemsOnly"] = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publisher_id"": 1234
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher_id"": 2345
                }
              ]
            }",
            ["RequestNestedPaginationQueries"] = @"{
              ""items"": [
                {
                  ""title"": ""Also Awesome book"",
                  ""publisher"": {
                    ""name"": ""Big Company"",
                    ""paginatedBooks"": {
                      ""items"": [
                        {
                          ""id"": 1,
                          ""title"": ""Awesome book""
                        },
                        {
                          ""id"": 2,
                          ""title"": ""Also Awesome book""
                        }
                      ],
                      ""hasNextPage"": false
                    }
                  }
                },
                {
                  ""title"": ""Great wall of china explained"",
                  ""publisher"": {
                    ""name"": ""Small Town Publisher"",
                    ""paginatedBooks"": {
                      ""items"": [
                        {
                          ""id"": 3,
                          ""title"": ""Great wall of china explained""
                        },
                        {
                          ""id"": 4,
                          ""title"": ""US history in a nutshell""
                        }
                      ],
                      ""hasNextPage"": false
                    }
                  }
                }
              ],
              ""endCursor"": ""eyJpZCI6M30="",
              ""hasNextPage"": true
            }"
        };
    }
}
