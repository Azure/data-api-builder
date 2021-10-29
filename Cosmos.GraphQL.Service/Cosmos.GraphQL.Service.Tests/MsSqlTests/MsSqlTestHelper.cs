using Cosmos.GraphQL.Service.configurations;
using Cosmos.GraphQL.Service.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.IO;

namespace Cosmos.GraphQL.Service.Tests.MsSql
{
    /// <summary>
    /// Helper functions for setting up test scenarios
    /// </summary>
    public class MsSqlTestHelper
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
        private static Lazy<IOptions<DataGatewayConfig>> _dataGatewayConfig = new Lazy<IOptions<DataGatewayConfig>>(() => MsSqlTestHelper.LoadConfig());

        /// <summary>
        /// Converts Raw JSON resolver to Resolver class object
        /// </summary>
        /// <param name="rawResolverText">escaped JSON string</param>
        /// <returns>GraphQLQueryResolver object</returns>
        public static GraphQLQueryResolver GetQueryResolverJson(string rawResolverText)
        {
            return JsonConvert.DeserializeObject<GraphQLQueryResolver>(rawResolverText);
        }

        /// <summary>
        /// Sets up configuration object as defined by appsettings.ENV.json file
        /// </summary>
        /// <returns></returns>
        private static IOptions<DataGatewayConfig> LoadConfig()
        {
            var datagatewayConfig = new DataGatewayConfig();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.Test.json")
                .Build();

            config.Bind(nameof(DataGatewayConfig), datagatewayConfig);

            return Options.Create(datagatewayConfig);
        }

        /// <summary>
        /// Returns configuration value loaded from file.
        /// </summary>
        public static IOptions<DataGatewayConfig> DataGatewayConfig
        {
            get { return _dataGatewayConfig.Value; }
        }
    }
}
