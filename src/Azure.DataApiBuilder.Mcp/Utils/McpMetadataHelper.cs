// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable IDE0005 // Using directive is unnecessary (analyzer noise)
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0005

namespace Azure.DataApiBuilder.Mcp.Utils
{
    public static class McpMetadataHelper
    {
        public static bool TryResolveMetadata(
            string entityName,
            RuntimeConfig config,
            IServiceProvider serviceProvider,
            out Azure.DataApiBuilder.Core.Services.ISqlMetadataProvider sqlMetadataProvider,
            out DatabaseObject dbObject,
            out string dataSourceName,
            out string error)
        {
            sqlMetadataProvider = default!;
            dbObject = default!;
            dataSourceName = string.Empty;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(entityName))
            {
                error = "Entity name cannot be null or empty.";
                return false;
            }

            var metadataProviderFactory = serviceProvider.GetRequiredService<Azure.DataApiBuilder.Core.Services.MetadataProviders.IMetadataProviderFactory>();

            try
            {
                dataSourceName = config.GetDataSourceNameFromEntityName(entityName);
                sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
            }
            catch (Exception)
            {
                error = $"Entity '{entityName}' is not defined in the configuration.";
                return false;
            }

            if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? temp) || temp is null)
            {
                error = $"Entity '{entityName}' is not defined in the configuration.";
                return false;
            }

            dbObject = temp!;
            return true;
        }
    }
}
