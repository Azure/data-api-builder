using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Cosmos.GraphQL.Service.Tests.Sql
{
    class SqlTestHelper
    {
        public static readonly string GraphQLSchema = @"
                type Query {
                    characterList: [Character]
                    characterById (id : ID!): Character
                }
                type Character {
                    id : ID,
                    name : String,
                    type: String,
                    homePlanet: Int,
                    primaryFunction: String
                }
                ";

        public static readonly string CharacterListResolver = "{\r\n \"id\": \"characterList\",\r\n \"parametrizedQuery\": \"SELECT id, name, type, homePlanet, primaryFunction FROM character\"\r\n }";
        public static readonly string CharacterByIdResolver = "{\r\n \"id\": \"characterById\",\r\n \"parametrizedQuery\": \"SELECT id, name, type, homePlanet, primaryFunction FROM character WHERE id = @id\"\r\n}";

        public static GraphQLQueryResolver GetQueryResolverJson(string rawResolverText)
        {
            return JsonConvert.DeserializeObject<GraphQLQueryResolver>(rawResolverText);
        }

        private static Lazy<IOptions<DataGatewayConfig>> _dataGatewayConfig = new Lazy<IOptions<DataGatewayConfig>>(() => SqlTestHelper.LoadConfig());

        private static IOptions<DataGatewayConfig> LoadConfig()
        {
            DataGatewayConfig datagatewayConfig = new DataGatewayConfig();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.MsSqlIntegrationTest.json")
                .Build();

            config.Bind(nameof(DataGatewayConfig), datagatewayConfig);

            return Options.Create(datagatewayConfig);
        }

        public static IOptions<DataGatewayConfig> DataGatewayConfig
        {
            get { return _dataGatewayConfig.Value; }
        }
    }
}
