using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
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
        /// FindById action serving the HttpGet verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets it content from the route attribute {*primaryKeyRoute}.
        /// asterisk(*) here is a wild-card/catch all i.e it matches the rest of the route after {entityName}.
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// CosmosDb: URL template: /<entityName>/[<shard_key>/<shard_value>]/[<id_key>/]<id_key_value>
        /// MsSql/PgSql: URL template: /<entityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL example: /SalesOrders/customerName/Xyz/saleOrderId/123 </param>
        [HttpGet]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<IActionResult> FindById(
            string entityName,
            string primaryKeyRoute)
        {
            try
            {
                string queryString = HttpContext.Request.QueryString.ToString();

                // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
                ClaimsIdentity identity = AppServiceAuthentication.Parse(this.HttpContext);
                if (identity != null)
                {
                    this.HttpContext.User = new ClaimsPrincipal(identity);
                }

                //Utilizes C#8 using syntax which does not require brackets.
                using JsonDocument result = await _restService.ExecuteFindAsync(entityName, primaryKeyRoute, queryString);

                if(result != null)
                {
                    //Clones the root element to a new JsonElement that can be
                    //safely stored beyond the lifetime of the original JsonDocument.
                    JsonElement resultElement = result.RootElement.Clone();
                    return Ok(resultElement);
                }else
                {
                    return NotFound();
                }

            }
            catch (PrimaryKeyValidationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (BadHttpRequestException ex)
            {
                Console.Error.WriteLine(ex.StackTrace);
                return new UnauthorizedResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.StackTrace);
                return StatusCode(statusCode: 500);
            }
        }
    }
}
