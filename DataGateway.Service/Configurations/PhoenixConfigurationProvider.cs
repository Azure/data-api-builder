using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Azure.DataGateway.Service.Configurations
{
    public class PhoenixConfigurationProvider : ConfigurationProvider
    {
        public PhoenixConfigurationProvider()
        {

        }

        public override void Set(string key, string value)
        {
            base.Set(key, value);
        }

        public void SetMany(IEnumerable<KeyValuePair<string, string>> properties)
        {
            foreach (KeyValuePair<string, string> kvp in properties)
            {
                base.Set(kvp.Key, kvp.Value);
            }

            OnReload();
        }
    }

    public static class ConfigurationBuilderExtensions
    {
        public static IConfigurationBuilder AddPhoenixConfiguration(this IConfigurationBuilder builder) => builder.Add(new PhoenixConfigurationSource());
    }

    public class PhoenixConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new PhoenixConfigurationProvider();
        }
    }
}
