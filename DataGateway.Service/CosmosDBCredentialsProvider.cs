using System;
using System.Threading.Tasks;
using Microsoft.Azure.Management.CosmosDB.Fluent;
using Microsoft.Azure.Management.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent;
using Microsoft.Azure.Management.ResourceManager.Fluent.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace Azure.DataGateway.Service
{
    public sealed class CosmosDBAccountCredentials
    {
        public string DocumentEndpoint { get; set; }

        public string Key { get; set; }
    }

    public static class CosmosDBCredentialsProvider
    {
        public static async Task<CosmosDBAccountCredentials> GetCosmosDbCredentialsAsync(string subscriptionId, string resourceGroupName, string accountName, string token, string tenant)
        {
            if (string.IsNullOrWhiteSpace(subscriptionId) ||
                string.IsNullOrWhiteSpace(accountName) ||
                string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException();
            }

            var credentials = new AzureCredentials(
                new TokenCredentials(token),
                new TokenCredentials(token),
                tenant,
                AzureEnvironment.AzureGlobalCloud
                );

            Microsoft.Azure.Management.Fluent.Azure.IAuthenticated authenticated = Microsoft.Azure.Management.Fluent.Azure.Configure().Authenticate(credentials);
            IAzure azureResourceManager = authenticated.WithSubscription(subscriptionId);
            ICosmosDBAccount cosmosDBAccount = await azureResourceManager.CosmosDBAccounts.GetByResourceGroupAsync(resourceGroupName, accountName);
            IDatabaseAccountListKeysResult keys = await cosmosDBAccount.ListKeysAsync();
            return new CosmosDBAccountCredentials
            {
                DocumentEndpoint = cosmosDBAccount?.DocumentEndpoint,
                Key = keys?.PrimaryMasterKey ?? keys?.SecondaryMasterKey ?? keys?.PrimaryReadonlyMasterKey ?? keys?.SecondaryReadonlyMasterKey
            };
        }
    }
}
