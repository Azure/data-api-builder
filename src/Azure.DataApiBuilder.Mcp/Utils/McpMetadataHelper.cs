// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Exceptions; // Added for DataApiBuilderException
using Microsoft.Extensions.DependencyInjection;

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

            // Resolve datasource name for the entity.
            try
            {
                dataSourceName = config.GetDataSourceNameFromEntityName(entityName);
            }
            catch (DataApiBuilderException dabEx) when (dabEx.SubStatusCode == DataApiBuilderException.SubStatusCodes.EntityNotFound)
            {
                error = $"Entity '{entityName}' is not defined in the configuration.";
                return false;
            }
            catch (DataApiBuilderException dabEx)
            {
                // Other DAB exceptions during entity->datasource resolution.
                error = dabEx.Message;
                return false;
            }

            // Resolve metadata provider for the datasource.
            try
            {
                sqlMetadataProvider = metadataProviderFactory.GetMetadataProvider(dataSourceName);
            }
            catch (DataApiBuilderException dabEx) when (dabEx.SubStatusCode == DataApiBuilderException.SubStatusCodes.DataSourceNotFound)
            {
                error = $"Data source '{dataSourceName}' for entity '{entityName}' is not defined in the configuration.";
                return false;
            }
            catch (DataApiBuilderException dabEx)
            {
                // Other DAB exceptions during metadata provider resolution.
                error = dabEx.Message;
                return false;
            }

            // Validate entity exists in metadata mapping.
            if (!sqlMetadataProvider.EntityToDatabaseObject.TryGetValue(entityName, out DatabaseObject? temp) || temp is null)
            {
                error = $"Entity '{entityName}' is not defined in the configuration.";
                return false;
            }

            dbObject = temp;
            return true;
        }
    }
}
