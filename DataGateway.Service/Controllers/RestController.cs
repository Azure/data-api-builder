using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Controllers
{
    /// <summary>
    /// Controller to serve REST Api requests for the route /entityName.
    /// </summary>
    [ApiController]
    [Route("{entityName}")]
    public class RestController : ControllerBase
    {
        /// <summary>
        /// Service providing REST Api executions.
        /// </summary>
        private readonly RestService _restService;

        /// <summary>
        /// Constructor.
        /// </summary>
        public RestController(RestService restService)
        {
            _restService = restService;
        }

        /// <summary>
        /// FindById action serving the HttpGet verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// CosmosDb: URL template: /<EntityName></EntityName>/[<shard_key>/<shard_value>]/[<id_key>/]<id_key_value>
        /// MsSql/PgSql: URL template: /<EntityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL example: /SalesOrders/customerName/Xyz/saleOrderId/123 </param>
        [HttpGet]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<JsonDocument> FindById(
            string entityName,
            string primaryKeyRoute)
        {
            var resultJson = JsonDocument.Parse(@"{ ""error"": ""FindMany is not supported yet.""}");
            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                string queryString = HttpContext.Request.QueryString.ToString();
                resultJson = await _restService.ExecuteFindAsync(entityName, primaryKeyRoute, queryString);
            }

            return resultJson;
        }
    }
}
