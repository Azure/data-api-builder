using System;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    /// <summary>
    /// Controller to serve REST Api requests for the route /entityName.
    /// This controller should adhere to the
    /// <see href="https://github.com/Microsoft/api-guidelines/blob/vNext/Guidelines.md">Microsoft REST API Guidelines</see>.
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
        /// String representing the value associated with "code" for a server error
        /// </summary>
        public const string SERVER_ERROR = "While processing your request the server ran into an unexpected error.";

        /// <summary>
        /// Constructor.
        /// </summary>
        public RestController(RestService restService)
        {
            _restService = restService;
        }

        /// <summary>
        /// Helper function returns a JsonResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="code">One of a server-defined set of error codes.</param>
        /// <param name="message">string provides a message associated with this error.</param>
        /// <param name="status">int provides the http response status code associated with this error</param>
        /// <returns></returns>
        public static JsonResult ErrorResponse(string code, string message, int status)
        {
            return new JsonResult(new
            {
                error = new
                {
                    code = code,
                    message = message,
                    status = status
                }
            });
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
            return await HandleOperation(
                entityName,
                Operation.Find,
                primaryKeyRoute);
        }

        /// <summary>
        /// Insert action serving the HttpPost verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// Expected URL template is of the following form:
        /// CosmosDb/MsSql/PgSql: URL template: /<entityName>
        /// URL MUST NOT contain a queryString
        /// URL example: /SalesOrders </param>
        [HttpPost]
        [Produces("application/json")]
        public async Task<IActionResult> Insert(
            string entityName)
        {
            return await HandleOperation(
                entityName,
                Operation.Insert,
                primaryKeyRoute: null);
        }

        /// <summary>
        /// Handle the given operation.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="operationType">The kind of operation to handle.</param>
        /// <param name="primaryKeyRoute">The string identifying the primary key route
        /// Its value could be null depending on the kind of operation.</param>
        private async Task<IActionResult> HandleOperation(
            string entityName,
            Operation operationType,
            string primaryKeyRoute = null)
        {
            try
            {
                // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
                ClaimsIdentity identity = AppServiceAuthentication.Parse(this.HttpContext);
                if (identity != null)
                {
                    this.HttpContext.User = new ClaimsPrincipal(identity);
                }

                // Utilizes C#8 using syntax which does not require brackets.
                using JsonDocument result
                    = await _restService.ExecuteAsync(
                            entityName,
                            operationType,
                            primaryKeyRoute);

                if (result != null)
                {
                    // Clones the root element to a new JsonElement that can be
                    // safely stored beyond the lifetime of the original JsonDocument.
                    JsonElement resultElement = result.RootElement.Clone();
                    return Ok(resultElement);
                }
                else
                {
                    return NotFound();
                }

            }
            catch (DatagatewayException ex)
            {
                Response.StatusCode = ex.StatusCode;
                return ErrorResponse(ex.SubStatusCode.ToString(), ex.Message, ex.StatusCode);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return ErrorResponse(
                    DatagatewayException.SubStatusCodes.UnexpectedError.ToString(),
                    SERVER_ERROR,
                    (int)HttpStatusCode.InternalServerError);
            }
        }
    }
}
