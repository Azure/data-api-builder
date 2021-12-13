using Azure.DataGateway.Service.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service
{
    internal sealed class CosmosDbConfiguration
    {
        public static CosmosDbConfiguration Instance { get; private set; }

        public string CosmosEndpoint { private set; get; }

        public string CosmosKey { private set; get; }

        public string DbAccountName { private set; get; }

        public string AadToken { set; get; }

        public string UserName { private set; get; }

        public string UserId { private set; get; }

        public string TenantId { private set; get; }

        public string SubscriptionId { private set; get; }

        public string ResourceGroup { private set; get; }

        public CosmosDbConfiguration()
        {
            if (Instance != null)
            {
                throw new Exception();
            }

            Instance = this;
        }

        public async Task InitializeAsync(BindRequest bindRequest, string aadToken)
        {
            string token = TokenUtils.GetRawAadToken(aadToken);
            string tenantId = TokenUtils.GetTenantId(token);

            CosmosDBAccountCredentials credentials = await CosmosDBCredentialsProvider.GetCosmosDbCredentialsAsync(
                bindRequest.SubscriptionId,
                bindRequest.ResourceGroup,
                bindRequest.DbAccountName,
                token,
                tenantId);

            this.AadToken = token;
            this.TenantId = tenantId;
            this.UserId = TokenUtils.GetUserId(this.AadToken);
            this.UserName = TokenUtils.GetUserName(this.AadToken);
            this.SubscriptionId = bindRequest.SubscriptionId;
            this.ResourceGroup = bindRequest.ResourceGroup;
            this.CosmosEndpoint = bindRequest.CosmosEndpoint;
            this.DbAccountName = bindRequest.DbAccountName;
            this.CosmosKey = credentials.Key;
        }

        public async Task RefreshAadToken(AadTokenRefreshRequest aadTokenRefreshRequest)
        {
            string aadToken = TokenUtils.GetRawAadToken(aadTokenRefreshRequest.AadToken);
            string userId = TokenUtils.GetUserId(aadToken);
            string tenantId = TokenUtils.GetTenantId(aadToken);

            CosmosDBAccountCredentials credentials = await CosmosDBCredentialsProvider.GetCosmosDbCredentialsAsync(
                this.SubscriptionId,
                this.ResourceGroup,
                this.DbAccountName,
                aadToken,
                tenantId);

            if (!this.UserId.Equals(userId))
            {
                throw new ArgumentException($"UserId  {userId} doesn't match with container's UserId");
            }

            this.AadToken = aadToken;
            this.CosmosKey = credentials.Key;
            this.TenantId = tenantId;
            this.UserId = userId;
        }
    }
}
