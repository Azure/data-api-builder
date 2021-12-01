using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GraphQLController : ControllerBase
    {
        private readonly GraphQLService _schemaManager;

        public GraphQLController(GraphQLService schemaManager)
        {
            _schemaManager = schemaManager;
        }

        // We return JsonElement instead of JsonDocument here
        // in order to dispose of the JsonDocument. We clone the root
        // element of the JsonDocument and return this JsonElement.
        [HttpPost]
        public async Task<JsonElement> PostAsync()
        {
            // TODO: We probably don't even need to keep track of "connections" since these are more requests but this will do for now.
            lock (Startup.CurrentConnectionsLock)
            {
                Startup.CurrentConnections++;
                Startup.LastActivity = DateTime.UtcNow;
            }

            string requestBody;
            using (StreamReader reader = new(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
            Dictionary<string, object> requestProperties = new();
            ClaimsIdentity? identity = AppServiceAuthentication.Parse(this.HttpContext);
            if (identity != null)
            {
                this.HttpContext.User = new ClaimsPrincipal(identity);

                // ClaimsPrincipal object must be added as a request property so HotChocolate
                // recognizes the authenticated user.
                requestProperties.Add(nameof(ClaimsPrincipal), this.HttpContext.User);
            }

            // JsonElement returned so that JsonDocument is disposed when thread exits
            string resultJson = await this._schemaManager.ExecuteAsync(requestBody, requestProperties);
            using JsonDocument jsonDoc = JsonDocument.Parse(resultJson);

            lock (Startup.CurrentConnectionsLock)
            {
                Startup.CurrentConnections--;
            }

            return jsonDoc.RootElement.Clone();
        }
    }
}
