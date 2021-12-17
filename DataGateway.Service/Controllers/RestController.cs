using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

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
        /// Find action serving the HttpGet verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets it content from the route attribute {*primaryKeyRoute}.
        /// asterisk(*) here is a wild-card/catch all i.e it matches the rest of the route after {entityName}.
        /// primary_key = [shard_value/]id_key_value
        /// primaryKeyRoute will be empty for FindOne or FindMany
        /// Expected URL template is of the following form:
        /// CosmosDb: URL template: /<entityName>/[<shard_key>/<shard_value>]/[<id_key>/]<id_key_value>
        /// MsSql/PgSql: URL template: /<entityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL may also contain a queryString
        /// URL example: /SalesOrders/customerName/Xyz/saleOrderId/123 </param>
        [HttpGet]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<IActionResult> Find(
            string entityName,
            string primaryKeyRoute)
        {
            try
            {
                string queryString = HttpContext.Request.QueryString.ToString();

                //Utilizes C#8 using syntax which does not require brackets.
                using JsonDocument result = await _restService.ExecuteFindAsync(entityName, primaryKeyRoute, queryString);

                //Clones the root element to a new JsonElement that can be
                //safely stored beyond the lifetime of the original JsonDocument.
                JsonElement resultElement = result.RootElement.Clone();
                return Ok(resultElement);
            }
            catch (DatagatewayException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return StatusCode(statusCode: 500);
            }
        }
    }
}
