using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    class TestRuntimeConfigProvider : IRuntimeConfigProvider
    {
        private const string MOCK_RUNTIME_CONFIG = @"
{
""$schema"": ""../../project-hawaii/playground/hawaii.draft-01.schema.json"",
  ""data-source"": {
    ""database-type"": ""cosmos"",
    ""connection-string"": """"
  },
  ""entities"": {
    ""Planet"": {
      ""graphql"": true
    },
    ""Character"": {
      ""graphql"": true
    }
  }
}
";

        public RuntimeConfig GetRuntimeConfig()
        {
            return DataGatewayConfig.GetDeserializedConfig<RuntimeConfig>(MOCK_RUNTIME_CONFIG);
        }
    }
}
