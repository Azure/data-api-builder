using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Controllers
{
    [ApiController]
    [Route("{entityName}")]
    public class RestController : ControllerBase
    {
        private readonly RestService _restService;

        public RestController(RestService restService)
        {
            _restService = restService;
        }

        [HttpGet]
        [Route("{*queryByPrimaryKey}")]
        [Produces("application/json")]
        public async Task<JsonDocument> FindById(
            string entityName,
            string queryByPrimaryKey)
        {
            string queryString = HttpContext.Request.QueryString.ToString();

            JsonDocument resultJson = await _restService.ExecuteAsync(entityName, queryByPrimaryKey, queryString);
            return resultJson;
        }
    }
}
