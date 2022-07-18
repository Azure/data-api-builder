using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("[controller]")]
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
            string requestBody;
            using (StreamReader reader = new(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            // Ensures the validated client role header value is present in the HotChocolate request context (IMiddlewareContext)
            // When used by downstream authorization handlers and resolvers via Dependency Injection.
            Dictionary<string, object> requestProperties = new();
            if (HttpContext.Request.Headers.TryGetValue(AuthorizationResolver.CLIENT_ROLE_HEADER, out StringValues clientRoleHeader))
            {
                requestProperties.Add(key: AuthorizationResolver.CLIENT_ROLE_HEADER, value: clientRoleHeader);
            }

            // ClaimsPrincipal object must be added as a request property so HotChocolate
            // recognizes the authenticated user. Anonymous requests are possible so check
            // for the HttpContext.User existence is necessary.
            if (this.HttpContext.User.Identity != null && this.HttpContext.User.Identity.IsAuthenticated)
            {
                requestProperties.Add(nameof(ClaimsPrincipal), this.HttpContext.User);
            }

            // JsonElement returned so that JsonDocument is disposed when thread exits
            string resultJson = await this._schemaManager.ExecuteAsync(requestBody, requestProperties);
            using JsonDocument jsonDoc = JsonDocument.Parse(resultJson);
            return jsonDoc.RootElement.Clone();
        }
    }
}
