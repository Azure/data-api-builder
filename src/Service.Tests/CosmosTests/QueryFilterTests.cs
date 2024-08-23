// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using QueryBuilder = Azure.DataApiBuilder.Service.GraphQLBuilder.Queries.QueryBuilder;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{

    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class QueryFilterTests : TestBase
    {
        private static int _pageSize = 10;
        private static readonly string _graphQLQueryName = "planets";
        private List<string> _idList;

        [TestInitialize]
        public void TestFixtureSetup()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            cosmosClient.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, 10);
        }

        /// <summary>
        /// Tests eq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEq()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {eq: ""Endor""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name = \"Endor\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);

        }

        /// <summary>
        /// Tests eq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringMultiLevelFiltersWithObjectType()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {character: {star: {name: {eq: ""Earth_star""}}}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.character.star.name = \"Earth_star\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where additionalAttributes is an array
        /// </summary>
        [TestMethod]
        public async Task TestStringMultiFiltersWithAndCondition()
        {
            // Get only the planets where the additionalAttributes array contains an object with name "volcano1"
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {
                        and: [
                            { additionalAttributes: {name: {eq: ""volcano1""}}}
                            { moons: {name: {eq: ""1 moon""}}}
                            { moons: {details: {contains: ""11""}}}
                        ]   
                     })
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name " +
                                     "FROM c " +
                                     "WHERE (EXISTS (SELECT 1 " +
                                                    "FROM table0 IN c.additionalAttributes " +
                                                    "WHERE table0.name = \"volcano1\" ) AND " +
                                            "EXISTS (SELECT 1 " +
                                                    "FROM table2 IN c.moons " +
                                                    "WHERE table2.name = \"1 moon\" ) AND " +
                                            "EXISTS (SELECT 1 " +
                                                    "FROM table4 IN c.moons " +
                                                    "WHERE table4.details LIKE \"%11%\" )  )";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where additionalAttributes is an array
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersOnArrayType()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {additionalAttributes: {name: {eq: ""volcano1""}}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name FROM c " +
                "JOIN a IN c.additionalAttributes " +
                "WHERE a.name = \"volcano1\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where moons is an array and moonAdditionalAttributes is a subarray
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersOnNestedArrayType()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {moons: {moonAdditionalAttributes: {name: {eq: ""moonattr0""}}}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name FROM c " +
                                     "WHERE EXISTS (SELECT 1 " +
                                                   "FROM table0 IN c.moons " +
                                                   "WHERE EXISTS (SELECT 1 " +
                                                                 "FROM table1 IN table0.moonAdditionalAttributes " +
                                                                 "WHERE table1.name = \"moonattr0\" ))";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where moons is an array and moonAdditionalAttributes is a subarray and moreAttributes is subarray with Alias
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersOnTwoLevelNestedArrayType()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
               @" : {moons: {moonAdditionalAttributes: {moreAttributes: {name: {eq: ""moonattr0""}}}}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name FROM c " +
                                     "WHERE EXISTS(SELECT 1 " +
                                                  "FROM table0 IN c.moons " +
                                                  "WHERE EXISTS(SELECT 1 " +
                                                               "FROM table1 IN table0.moonAdditionalAttributes " +
                                                               "WHERE EXISTS(SELECT 1 " +
                                                                            "FROM table2 IN table1.moreAttributes " +
                                                                            "WHERE table2.name = \"moonattr0\")))";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where moons is an array and moonAdditionalAttributes is a subarray With AND condition
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersOnNestedArrayTypeHavingAndCondition()
        {
            // Get only the planets where the additionalAttributes array contains an object with name "volcano1"
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {moons: {
                                and:[
                                      {moonAdditionalAttributes: {name: {eq: ""moonattr0""}}}
                                      {name: {eq: ""0 moon""}}
                                ]
                     }})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name FROM c " +
                "JOIN a IN c.moons " +
                "JOIN b IN a.moonAdditionalAttributes " +
                "WHERE b.name = \"moonattr0\" and a.name = \"0 moon\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where additionalAttributes is an array
        /// </summary>
        [TestMethod]
        public async Task TestStringMultiFiltersOnArrayTypeWithAndCondition()
        {
            // Get only the planets where the additionalAttributes array contains an object with name "volcano1"
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {additionalAttributes: {name: {eq: ""volcano1""}}
                and: { moons: {name: {eq: ""1 moon""}}}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name FROM c " +
                "JOIN a IN c.additionalAttributes " +
                "JOIN b IN c.moons " +
                "WHERE a.name = \"volcano1\" and b.name = \"1 moon\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        /// <summary>
        /// Tests eq of StringFilterInput where additionalAttributes is an array
        /// </summary>
        [TestMethod]
        public async Task TestStringMultiFiltersOnArrayTypeWithOrCondition()
        {
            // Get only the planets where the additionalAttributes array contains an object with name "volcano1"
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : { or: [
                {additionalAttributes: {name: {eq: ""volcano1""}}}
                { moons: {name: {eq: ""1 moon""}}}]})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQueryWithJoin = "SELECT c.name " +
                                     "FROM c " +
                                     "WHERE (EXISTS (SELECT 1 " +
                                                    "FROM table0 IN c.additionalAttributes " +
                                                    "WHERE table0.name = \"volcano1\" ) OR " +
                                            "EXISTS (SELECT 1 " +
                                                    "FROM table2 IN c.moons " +
                                                    "WHERE table2.name = \"1 moon\" ) )";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQueryWithJoin);
        }

        private async Task ExecuteAndValidateResult(string graphQLQueryName, string gqlQuery, string dbQuery, bool ignoreBlankResults = false, Dictionary<string, object> variables = null)
        {
            string authToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: AuthorizationType.Authenticated.ToString());
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQueryName, query: gqlQuery, authToken: authToken, variables: variables);
            JsonDocument expected = await ExecuteCosmosRequestAsync(dbQuery, _pageSize, null, _containerName);
            ValidateResults(actual.GetProperty("items"), expected.RootElement, ignoreBlankResults);
        }

        private static void ValidateResults(JsonElement actual, JsonElement expected, bool ignoreBlankResults)
        {
            Assert.IsNotNull(expected);
            Assert.IsNotNull(actual);

            if (!ignoreBlankResults)
            {
                // Making sure we are not asserting empty results
                Assert.IsFalse(expected.ToString().Equals("[]"), "Expected  Response is Empty.");
                Assert.IsFalse(actual.ToString().Equals("[]"), "Actual  Response is Empty.");
            }

            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(actual.ToString()), JToken.Parse(expected.ToString())));
        }

        /// <summary>
        /// Tests neq of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNeq()
        {

            string gqlQuery = @"{
                planets(first: 10," + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {neq: ""Endor""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name != \"Endor\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests startsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersStartsWith()
        {
            string gqlQuery = @"{
                planets(first: 10," + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {startsWith: ""En""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name LIKE \"En%\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests endsWith of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersEndsWith()
        {
            string gqlQuery = @"{
                planets(first: 10," + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {endsWith: ""h""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name LIKE \"%h\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests contains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersContains()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {contains: ""pi""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name LIKE \"%pi%\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests notContains of StringFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersNotContains()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
                @" : {name: {notContains: ""pi""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name NOT LIKE \"%pi%\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests that special characters are escaped in operations involving LIKE
        /// Special chars not working so ignoring for now!
        /// </summary>
        [Ignore]
        [TestMethod]
        public async Task TestStringFiltersContainsWithSpecialChars()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {name: {contains: ""%""}})
                {
                    items {
                        name
                    }
                }
            }";

            string dbQuery = "select c.name from c where c.name LIKE \"%\"";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests eq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersEq()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {eq: 4}})
                {
                    items {
                        age
                    }
                }
            }";

            string dbQuery = "select c.age from c where c.age = 4";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests neq of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersNeq()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {neq: 4}})
                {
                    items {
                        age
                    }
                }
            }";

            string dbQuery = "select c.age from c where c.age != 4";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests gt and lt of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGtLt()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {gt: 2 lt: 5}})
                {
                    items {
                        age
                    }
                }
            }";

            string dbQuery = "select c.age from c where c.age > 2 and c.age < 5";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests gte and lte of IntFilterInput
        /// </summary>
        [TestMethod]
        public async Task TestIntFiltersGteLte()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {gte: 2 lte: 5}})
                {
                    items {
                        age
                    }
                }
            }";

            string dbQuery = "select c.age from c where c.age >= 2 and c.age <= 5";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test that:
        /// - the predicate equivalent of *FilterInput input types is put in parenthesis if the
        ///   predicate
        /// - the predicate equivalent of and / or field is put in parenthesis if the predicate
        ///   contains only one operation
        /// </summary>
        /// <remarks>
        /// one operation predicate: id == 2
        /// multiple operation predicate: id == 2 AND publisher_id < 3
        /// </remarks>
        [TestMethod]
        public async Task TestCreatingParenthesis1()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    name: {contains: ""En""}
                                    or: [
                                        {age:{gt: 2 lt: 4}},
                                        {age: {gte: 2}},
                                    ]
                                })
                {
                    items{
                        name
                        age
                    }
                }
            }";

            string dbQuery = "SELECT c.name, c.age FROM c WHERE (c.name LIKE \"En%\" " +
                "AND ((c.age > 2 AND c.age < 4) OR c.age >=2))";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test that:
        /// - the predicate equivalent of *FilterInput input types is put in parenthesis if the
        ///   predicate
        /// - the predicate equivalent of and / or field is put in parenthesis if the predicate
        ///   contains only one operation
        /// </summary>
        /// <remarks>
        /// one operation predicate: id == 2
        /// multiple operation predicate: id == 2 AND publisher_id < 3
        /// </remarks>
        [TestMethod]
        public async Task TestCreatingParenthesis2()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    or: [
                                        {age: {gt: 2} and: [{age: {lt: 4}}]},
                                        {age: {gte: 2} name: {contains: ""En""}}
                                    ]
                                })
                {
                    items{
                        name
                        age
                    }
                }
            }";
            string dbQuery = "SELECT c.name, c.age FROM c WHERE" +
                " ((c.age > 2 AND c.age < 4) OR (c.age >= 2 AND c.name LIKE \"En%\"))";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test that a complicated filter is evaluated as:
        /// - all non and/or fields of each *FilterInput are AND-ed together and put in parenthesis
        /// - each *FilterInput inside an and/or team is AND/OR-ed together and put in parenthesis
        /// - the final predicate is:
        ///   ((<AND-ed non and/or predicates>) AND (<AND-ed predicates in and filed>) OR <OR-ed predicates in or field>)
        /// </summart>
        ///
        [TestMethod]
        public async Task TestComplicatedFilter()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {
                                    age: {gte: 1}
                                    name: {notContains: ""En""}
                                    and: [
                                        {
                                            age: {lt: 6}
                                            name: {startsWith: ""Ma""}
                                        },
                                        {
                                            name: {endsWith: ""s""}
                                            age: {neq: 5}
                                        }
                                    ]
                                    or: [
                                        {dimension: {eq: ""space""}}
                                    ]
                                })
                {
                    items {
                        age
                        name
                        dimension
                    }
                }
            }";

            string dbQuery = "SELECT c.age, c.name, c.dimension FROM c " +
                "WHERE (c.age >= 1 AND c.name NOT LIKE \"%En\" AND" +
                " ((c.age < 6 AND c.name LIKE \"Ma%\") AND (c.name LIKE \"%s\" AND c.age != 5)) AND c.dimension = \"space\")";

            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test that an empty and evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyAnd()
        {
            string graphQLQueryName = "planets";
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {and: []})
                {
                    items {
                        id
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQueryName, query: gqlQuery);
            Assert.IsTrue(JToken.DeepEquals(actual.GetProperty("items").ToString(), "[]"));
        }

        /// <summary>
        /// Test that an empty or evaluates to False
        /// </summary>
        [TestMethod]
        public async Task TestOnlyEmptyOr()
        {
            string graphQLQueryName = "planets";
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {or: []})
                {
                    items {
                        id
                    }
                }
            }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLQueryName, query: gqlQuery);
            Assert.IsTrue(JToken.DeepEquals(actual.GetProperty("items").ToString(), "[]"));
        }

        /// <summary>
        /// Test filtering null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullIntFields()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {isNull: false}})
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "select c.name, c.age from c where NOT IS_NULL(c.age)";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test filtering non null integer fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullIntFields()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {isNull: true}})
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "select c.name, c.age from c where IS_NULL(c.age)";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery, ignoreBlankResults: true);
        }

        /// <summary>
        /// Test filtering null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNullStringFields()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {name: {isNull: true}})
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "select c.name, c.age from c where IS_NULL(c.name)";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery, ignoreBlankResults: true);
        }

        /// <summary>
        /// Test filtering not null string fields
        /// </summary>
        [TestMethod]
        public async Task TestGetNonNullStringFields()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {name: {isNull: false}})
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "select c.name, c.age from c where NOT IS_NULL(c.name)";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        ///
        [Ignore] //Todo: This test fails on linux/mac due to some string comparisoin issues.
        [TestMethod]
        public async Task TestExplicitNullFieldsAreIgnored()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {gte:2 lte: null}
                                                           name: null
                                                           or: null })
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "select c.name, c.age from c where c.age >= 2";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Passes null to nullable fields and makes sure they are ignored
        /// </summary>
        [TestMethod]
        public async Task TestInputObjectWithOnlyNullFieldsEvaluatesToFalse()
        {
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME + @" : {age: {lte: null}})
                {
                    items {
                        name
                        age
                    }
                }
            }";

            string dbQuery = "SELECT c.name, c.age FROM c WHERE 1 != 1";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery, ignoreBlankResults: true);
        }

        /// <summary>
        /// Test filters on nested object
        /// </summary>
        [TestMethod]
        public async Task TestFilterOnNestedFields()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {character : {name : {eq : ""planet character""}}})
                {
                    items {
                        id
                        name
                        character {
                                id
                                type
                                name
                                homePlanet
                                primaryFunction
                                star{
                                   name
                                }
                            }
                    }
                 }
            }";

            string dbQuery = "SELECT top 1 c.id, c.name, c.character FROM c where c.character.name = \"planet character\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test filters on nested object with and
        /// </summary>
        [TestMethod]
        public async Task TestFilterOnNestedFieldsWithAnd()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {character : {name : {eq : ""planet character""}}
                    and: [{name: {eq: ""Endor""}} ]  })
                {
                    items {
                        id
                        name
                        character {
                                id
                                type
                                name
                                homePlanet
                                primaryFunction
                                star{
                                   name
                                }
                            }
                    }
                 }
            }";

            string dbQuery = "SELECT top 1 c.id, c.name, c.character FROM c where c.character.name = \"planet character\" and c.name=\"Endor\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test filters on nested object
        /// </summary>
        [TestMethod]
        public async Task TestFilterOnInnerNestedFields()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {character : {star : {name : {eq : ""Endor_star""}}}})
                {
                    items {
                        id
                        name
                        character {
                                id
                                type
                                name
                                homePlanet
                                primaryFunction
                                star{
                                    name
                                }
                            }
                    }
                 }
            }";

            string dbQuery = "SELECT top 1 c.id, c.name, c.character FROM c where c.character.star.name = \"Endor_star\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Test filters when entity names are using alias.
        /// This exercises the scenario when top level entity name is using an alias,
        /// as well as the nested level entity name is using an alias,
        /// in both layers, the entity name to GraphQL type lookup is successfully performed.
        /// </summary>
        [TestMethod]
        public async Task TestFilterWithEntityNameAlias()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {stars : {tag: {name : {eq : ""tag1""}}}})
                {
                    items {
                        id
                    }
                 }
            }";

            string dbQuery = "SELECT top 1 c.id FROM c JOIN starAlias  IN c.stars where starAlias.tag.name = \"tag1\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// For "item-level-permission-role" role, DB policies are defined. This test confirms that all the DB policies are considered.
        /// For the reference, Below conditions are applied for an Entity in Db Config file.
        /// MoonAdditionalAttributes (array inside moon object which is an array in container): "@item.name eq 'moonattr0'"
        /// Earth(object in object): "@item.type eq 'earth0'"
        /// AdditionalAttribute (array in container): "@item.type eq 'volcano0'"
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuth_Only_AuthorizedArrayItem()
        {
            string gqlQuery = @"{
                 planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : { character: {type: {eq: ""Mars""}}})
                 {
                     items {
                         id
                     }
                 }
            }";

            // Now get the item with item level permission
            string clientRoleHeader = "item-level-permission-role";
            // string clientRoleHeader = "authenticated";
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                queryName: _graphQLQueryName,
                query: gqlQuery,
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            string dbQuery = $"SELECT c.id " +
                $"FROM c " +
                $"WHERE c.character.type = 'Mars' " +
                $"AND c.earth.type = 'earth0' " + // From DB Policy
                $"AND EXISTS (SELECT VALUE 1 " +
                            $"FROM  table1 IN c.additionalAttributes " +
                            $"WHERE (table1.name = 'volcano0')) " + // From DB Policy
                $"AND EXISTS (SELECT VALUE 1 " +
                            $"FROM  table2 IN c.moons " +
                            $"JOIN  table3 IN table2.moonAdditionalAttributes " +
                            $"WHERE (table3.name = 'moonattr0'))"; // From DB Policy

            JsonDocument expected = await ExecuteCosmosRequestAsync(dbQuery, _pageSize, null, _containerName);
            // Validate the result contains the GraphQL authorization error code.
            ValidateResults(actual.GetProperty("items"), expected.RootElement, false);
        }

        #region Field Level Auth
        /// <summary>
        /// Tests that the field level query filter succeeds requests when filter fields are authorized
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuth_AuthorizedField()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {earth: {id : {eq : """ + _idList[0] + @"""}}})
                {
                    items {
                        id
                    }
                }
            }";

            string dbQuery = $"SELECT top 1 c.id FROM c where c.earth.id = \"{_idList[0]}\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests that the field level query filter fails authorization when filter fields are
        /// unauthorized because the field 'name' on object type 'earth' is an excluded field of the read
        /// operation permissions defined for a role.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuth_UnauthorizedField()
        {
            // Run query
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {earth: {name : {eq : ""test name""}}})
                {
                    items {
                        name
                    }
                }
            }";
            string clientRoleHeader = "limited-read-role";
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: _graphQLQueryName,
                query: gqlQuery,
                variables: new() { { "name", "test name" } },
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE));
        }

        /// <summary>
        /// Tests that the field level query filter succeeds requests when filter fields are authorized
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuth_AuthorizedWildCard()
        {
            // Run query
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {name : {eq : ""Earth""}})
                {
                    items {
                        name
                    }
                }
            }";
            string clientRoleHeader = AuthorizationType.Anonymous.ToString();
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: "planets",
                query: gqlQuery,
                variables: new() { },
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            Assert.AreEqual(response.GetProperty("items")[0].GetProperty("name").ToString(), "Earth");
        }

        /// <summary>
        /// Tests that the nested field level query filter passes authorization when nested filter fields are authorized
        /// because the field 'id' on object type 'earth' is an included field of the read operation
        /// permissions defined for the anonymous role.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterNestedFieldAuth_AuthorizedNestedField()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {earth : {id : {eq : """ + _idList[0] + @"""}}})
                {
                    items {
                        earth {
                                id
                              }
                    }
                }
            }";

            string authToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: AuthorizationType.Authenticated.ToString());
            JsonElement actual = await ExecuteGraphQLRequestAsync(_graphQLQueryName, query: gqlQuery, authToken: authToken);
            Assert.AreEqual(actual.GetProperty("items")[0].GetProperty("earth").GetProperty("id").ToString(), _idList[0]);
        }

        /// <summary>
        /// Tests that the nested field level query filter fails authorization when nested filter fields are
        /// unauthorized because the field 'name' on object type 'earth' is an excluded field of the read
        /// operation permissions defined for the anonymous role.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterNestedFieldAuth_UnauthorizedNestedField()
        {
            // Run query
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {earth : {name : {eq : ""test name""}}})
                {
                    items {
                        id
                        name
                        earth {
                                name
                              }
                    }
                }
            }";

            string clientRoleHeader = "limited-read-role";
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: _graphQLQueryName,
                query: gqlQuery,
                variables: new() { { "name", "test name" } },
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE));
        }

        /// <summary>
        /// Tests that the nested field level query filter fails authorization when nested object is
        /// unauthorized. Here, Nested array type 'moreAttributes' is available for 'Authenticated' role only and
        /// we are trying to access it with 'anonymous' role.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterNestedArrayFieldAuth_UnauthorizedNestedField()
        {
            // Run query
            string gqlQuery = @"{
                planets(first: 10, " + QueryBuilder.FILTER_FIELD_NAME +
               @" : {moons: {moonAdditionalAttributes: {moreAttributes: {name: {eq: ""moonattr0""}}}}})
                {
                    items {
                        name
                    }
                }
            }";

            string clientRoleHeader = AuthorizationType.Anonymous.ToString();
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: _graphQLQueryName,
                query: gqlQuery,
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE));
        }

        /// <summary>
        /// This is for testing the scenario when the filter field is authorized, but the query field is unauthorized.
        /// For "type" field in "Earth" GraphQL type, it has @authorize(policy: "authenticated") directive in the test schema,
        /// but in the runtime config, this field is marked as included field for read operation with "limited-read-role" role,
        /// this should return unauthorized.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFieldAuthConflictingWithFilterFieldAuth_Unauthorized()
        {
            // Run query
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {earth: {id : {eq : """ + _idList[0] + @"""}}})
                {
                    items {
                        earth {
                            id
                            type
                        } 
                    }
                }
            }";

            string clientRoleHeader = "limited-read-role";
            string authToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader);
            JsonElement response = await ExecuteGraphQLRequestAsync(_graphQLQueryName,
                query: gqlQuery,
                authToken: authToken,
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains("The current user is not authorized to access this resource."));
        }

        /// <summary>
        /// Tests that the field level query filter succeeds requests
        /// when GraphQL is set to true without setting singular type in runtime config and
        /// when include fields are WILDCARD,
        /// all the columns are able to be retrieved for authorization validation.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuthWithoutSingularType()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {suns: {id : {eq : """ + _idList[0] + @"""}}})
                {
                    items {
                        id
                        name
                    }
                }
            }";

            string dbQuery = $"SELECT top 1 c.id, c.name FROM c JOIN sun IN c.suns where sun.id = \"{_idList[0]}\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests that the field level query filter failed authorization validation
        /// when include fields are WILDCARD and exclude fields specifies fields,
        /// exclude fields takes precedence over include fields.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterFieldAuth_ExcludeTakesPredecence()
        {
            string gqlQuery = @"{
                planets(first: 1, " + QueryBuilder.FILTER_FIELD_NAME + @" : {suns: { name : {eq : ""test name""}}})
                {
                    items {
                        suns {
                            id
                            name
                        }
                    }
                }
            }";

            string clientRoleHeader = AuthorizationType.Anonymous.ToString();
            JsonElement response = await ExecuteGraphQLRequestAsync(
                queryName: _graphQLQueryName,
                query: gqlQuery,
                variables: new() { { "name", "test name" } },
                authToken: AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader),
                clientRoleHeader: clientRoleHeader);

            // Validate the result contains the GraphQL authorization error code.
            string errorMessage = response.ToString();
            Assert.IsTrue(errorMessage.Contains(DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE));

        }

        /// <summary>
        /// Tests that the field level query filter work with list type for 'contains' operator
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterContains_WithStringArray()
        {
            string gqlQuery = @"{
                planets(" + QueryBuilder.FILTER_FIELD_NAME + @" : {tags: { contains : ""tag1""}})
                {
                    items {
                        id
                        name
                    }
                }
            }";

            string dbQuery = $"SELECT c.id, c.name FROM c where ARRAY_CONTAINS(c.tags, 'tag1')";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests that the field level query filter work with list type for 'notcontains' operator.
        /// </summary>
        [TestMethod]
        public async Task TestQueryFilterNotContains_WithStringArray()
        {
            string gqlQuery = @"{
                planets(" + QueryBuilder.FILTER_FIELD_NAME + @" : {tags: { notContains : ""tag3""}})
                {
                    items {
                        id
                        name
                    }
                }
            }";

            string dbQuery = $"SELECT c.id, c.name FROM c where NOT ARRAY_CONTAINS(c.tags, 'tag3')";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery);
        }

        /// <summary>
        /// Tests that the pk level query filter is working with variables.
        /// </summary>
        [TestMethod]
        public async Task TestQueryIdFilterField_WithVariables()
        {
            string gqlQuery = @"
            query ($id: ID) {
                    planets(" + QueryBuilder.FILTER_FIELD_NAME + @" : {id: {eq : $id}})
                    {
                        items {
                            id
                            name
                        }
                    }
                }
            ";

            string dbQuery = $"SELECT c.id, c.name FROM c where c.id = \"{_idList[0]}\"";
            await ExecuteAndValidateResult(_graphQLQueryName, gqlQuery, dbQuery, variables: new() { { "id", _idList[0] } });
        }
        #endregion

        [TestCleanup]
        public void TestFixtureTearDown()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            cosmosClient.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}
