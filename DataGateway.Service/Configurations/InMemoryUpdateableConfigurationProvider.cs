using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Azure.DataGateway.Service.Configurations
{
    /// <summary>
    /// In-memory implementation of <see cref="IConfigurationProvider"/> that supports updating
    /// multiple configs and change tracking.
    /// </summary>
    public class InMemoryUpdateableConfigurationProvider : ConfigurationProvider
    {
        public override void Set(string key, string value)
        {
            base.Set(key, value);
        }

        /// <summary>
        /// Update multiple properties and notifies listeners of the change after all
        /// properties have been set.
        /// </summary>
        /// <param name="properties">The properties to set.</param>
        public void SetManyAndReload(IEnumerable<KeyValuePair<string, string>> properties)
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
        public static IConfigurationBuilder AddInMemoryUpdateableConfiguration(this IConfigurationBuilder builder) => builder.Add(new InMemoryUpdateableConfigurationSource());
    }

    public class InMemoryUpdateableConfigurationSource : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new InMemoryUpdateableConfigurationProvider();
        }
    }
}
