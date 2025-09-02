// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;

namespace Azure.DataApiBuilder.Service.Mcp.Helpers
{
    public class McpUtility
    {
        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public McpUtility(RuntimeConfigProvider runtimeConfigProvider)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
        }

        /// <summary>
        /// Gets the list of entities that have entity health enabled from the runtime configuration.
        /// </summary>
        /// <returns>List of entities</returns>
        public List<string> GetEntitiesFromRuntime()
        {
            RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
            List<KeyValuePair<string, Entity>> enabledEntities = runtimeConfig.Entities.Entities
                .Where(e => e.Value.IsEntityHealthEnabled)
                .ToList();

            return enabledEntities.Select(e => e.Key).ToList();
        }
    }
}
