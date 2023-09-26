// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public class MultiSourceQueryExecutionUnitTests
    {
        string _query = @"{
                clients_by_pk(Index: 3) {
                    FirstName
                }
                customers_by_pk(Index: 3) {
                    Name
                }
            }";

        /// <summary>
        /// Validates successful execution of a query against multiple sources.
        /// </summary>
        [TestMethod]
        public async Task TestMultiSourceQuery()
        {
            string dataSourceName1 = "db1";
            string dataSourceName2 = "db2";
            string entityName1 = "Clients";
            string entityName2 = "Customers";
            string queryName1 = "clients_by_pk";
            string queryName2 = "customers_by_pk";

            Dictionary<string, Entity> entities = new()
            {
                { entityName1, GraphQLTestHelpers.GenerateEmptyEntity() },
                { entityName2, GraphQLTestHelpers.GenerateEmptyEntity() }
            };

            Dictionary<string, DataSource> dataSourceNameToDataSource = new()
            {
                { dataSourceName1, new(DatabaseType.MySQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new())},
                { dataSourceName2, new(DatabaseType.CosmosDB_NoSQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new()) }
            };

            Dictionary<string, string> entityNameToDataSourceName = new()
            {
                { entityName1, dataSourceName1 },
                { entityName2, dataSourceName2 }
            };

            RuntimeConfig mockConfig1 = new(
               Schema: "",
               DataSource: new(DatabaseType.MySQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Host: new(null, null)
               ),
               DefaultDataSourceName: dataSourceName1,
               DataSourceNameToDataSource: dataSourceNameToDataSource,
               EntityNameToDataSourceName: entityNameToDataSourceName,
               Entities: new(entities)
               );

            string jsonResult = "{\"FirstName\": \"db1\"}";

            JsonDocument document1 = JsonDocument.Parse(jsonResult);
            Tuple<JsonDocument, IMetadata> mockReturn1 = new(document1, PaginationMetadata.MakeEmptyPaginationMetadata());

            string jsonResult2 = "{\"Name\":\"db2\"}";
            JsonDocument document2 = JsonDocument.Parse(jsonResult2);
            Tuple<JsonDocument, IMetadata> mockReturn2 = new(document2, PaginationMetadata.MakeEmptyPaginationMetadata());

            Mock<IQueryEngine> sqlQueryEngine = new();
            sqlQueryEngine.Setup(x => x.ExecuteAsync(It.IsAny<IMiddlewareContext>(), It.IsAny<IDictionary<string, object>>(), dataSourceName1)).Returns(Task.FromResult(mockReturn1));

            Mock<IQueryEngine> cosmosQueryEngine = new();
            cosmosQueryEngine.Setup(x => x.ExecuteAsync(It.IsAny<IMiddlewareContext>(), It.IsAny<IDictionary<string, object>>(), dataSourceName2)).Returns(Task.FromResult(mockReturn2));

            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.MySQL)).Returns(sqlQueryEngine.Object);
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.CosmosDB_NoSQL)).Returns(cosmosQueryEngine.Object);

            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            Mock<RuntimeConfigLoader> mockLoader = new(null);
            mockLoader.Setup(x => x.TryLoadKnownConfig(out mockConfig1, It.IsAny<bool>())).Returns(true);

            RuntimeConfigProvider provider = new(mockLoader.Object);

            string graphQLSchema = File.ReadAllText("MultiSourceTestSchema.gql");
            ISchemaBuilder schemaBuilder = SchemaBuilder.New().AddDocumentFromString(graphQLSchema)
                .AddAuthorizeDirectiveType()
                .AddDirectiveType<ModelDirectiveType>() // Add custom directives used by DAB.
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDirectiveType<DefaultValueDirectiveType>()
                .AddDirectiveType<AutoGeneratedDirectiveType>()
                .AddType<OrderByType>()
                .AddType<DefaultValueType>()
                .Use((services, next) => new ResolverMiddleware(next, queryEngineFactory.Object, mutationEngineFactory.Object, provider));
            ISchema schema = schemaBuilder.Create();
            IExecutionResult result = await schema.MakeExecutable().ExecuteAsync(_query);

            Assert.AreEqual(1, sqlQueryEngine.Invocations.Count);
            Assert.AreEqual(1, cosmosQueryEngine.Invocations.Count);

            Assert.IsNull(result.Errors, "There should be no errors in processing of multisource query.");
            QueryResult queryResult = (QueryResult)result;
            Assert.IsNotNull(queryResult.Data, "Data should be returned for multisource query.");
            IReadOnlyDictionary<string, object> data = queryResult.Data;
            Assert.IsTrue(data.TryGetValue(queryName1, out object queryNode1));
            Assert.IsTrue(data.TryGetValue(queryName2, out object queryNode2));

            ResultMap queryMap1 = (ResultMap)queryNode1;
            ResultMap queryMap2 = (ResultMap)queryNode2;

            Assert.AreEqual("db1", queryMap1[0].Value);
            Assert.AreEqual("db2", queryMap2[0].Value);
        }
    }
}
