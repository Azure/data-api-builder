using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Services;
using Azure.DataGateway.Service.Models;
using Microsoft.AspNetCore.Mvc;

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

        [HttpPost]
        public async Task<JsonDocument> PostAsync()
        {
            string requestBody;
            using (var reader = new StreamReader(this.HttpContext.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
            Dictionary<string, object> requestProperties = new();
            ClaimsIdentity identity = AppServiceAuthentication.Parse(this.HttpContext);
            if (identity != null)
            {
                this.HttpContext.User = new ClaimsPrincipal(identity);

                // ClaimsPrincipal object must be added as a request property so HotChocolate
                // recognizes the authenticated user. 
                requestProperties.Add(nameof(ClaimsPrincipal), this.HttpContext.User);
            }

            string resultJson = await this._schemaManager.ExecuteAsync(requestBody, requestProperties);
            return JsonDocument.Parse(resultJson);
        }
    }
}
