using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Services;
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

            string resultJson = await this._schemaManager.ExecuteAsync(requestBody);
            return JsonDocument.Parse(resultJson);
        }
    }
}
