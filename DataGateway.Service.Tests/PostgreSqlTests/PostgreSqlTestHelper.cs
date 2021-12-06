using System;
using System.IO;
using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Azure.DataGateway.Service.Tests.PostgreSql {
    public class PostgreSqlTestHelper {
        private static Lazy<IOptions<DataGatewayConfig>> _dataGatewayConfig = new(() => PostgreSqlTestHelper.LoadConfig());

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
            DataGatewayConfig datagatewayConfig = new();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.PostgreSqlIntegrationTest.json")
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