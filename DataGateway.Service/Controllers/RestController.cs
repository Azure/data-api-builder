using System;
using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Http.Extensions;
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
        public static JsonResult ErrorResponse(string code, string message, HttpStatusCode status)
        {
            return new JsonResult(new
            {
                error = new
                {
                    code = code,
                    message = message,
                    status = (int)status
                }
            });
        }

        /// <summary>
        /// Helper function returns an OkObjectResult with provided arguments in a
        /// form that complies with vNext Api guidelines.
        /// </summary>
        /// <param name="jsonElement">Value representing the Json results of the client's request.</param>
        /// <param name="url">Value represents the complete url needed to continue with paged results.</param>
        /// <returns></returns>
        private OkObjectResult OkResponse(JsonElement jsonResult, string entityName)
        {
            string queryString = Request.QueryString.ToString();
            // this will only provide for nextlink when client asks for custom limit
            // but we really want to provide nextlink whenever there are more pages
            // one way would be to execute the query we pass back to client and see if there
            // are results, but this a poorly optimized approach.
            // something like HasNext that returns along with the query would be nice.
            if (!queryString.Contains("$first"))
            {
                return Ok(new
                {
                    value = jsonResult,
                });
            }

            string primaryKey = _restService.MetadataStoreProvider.GetTableDefinition(entityName).PrimaryKey[0].ToString();
            string? afterValue = jsonResult[jsonResult.GetArrayLength() - 1].GetProperty(primaryKey).ToString();
            NameValueCollection nvc = HttpUtility.ParseQueryString(queryString);
            queryString = "?";
            int count = nvc.Count;
            foreach (string key in nvc)
            {
                --count;
                string? value = nvc[key];
                if (string.Equals(key, "$after"))
                {
                    value = afterValue;
                }

                queryString += key + "=" + value;
                if (count > 0)
                {
                    queryString += "&";
                }

            }

            string root = "https://localhost:5001";
            return Ok(new
            {
                value = jsonResult,
                @nextLink = $"{root}{Request.Path}{queryString}"
            });
            ;
        }

        /// <summary>
        /// Find action serving the HttpGet verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets its content from the route attribute {*primaryKeyRoute}.
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
            string? primaryKeyRoute)
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
                primaryKeyRoute: string.Empty);
        }

        /// <summary>
        /// Delete action serving the HttpDelete verb.
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets its content from the route attribute {*primaryKeyRoute}.
        /// asterisk(*) here is a wild-card/catch all i.e it matches the rest of the route after {entityName}.
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// MsSql/PgSql: URL template: /<entityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: /Books </param>
        [HttpDelete]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<IActionResult> Delete(
            string entityName,
            string? primaryKeyRoute)
        {
            return await HandleOperation(
                entityName,
                Operation.Delete,
                primaryKeyRoute);
        }

        /// <summary>
        /// Replacement Update/Insert action serving the HttpPut verb
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets its content from the route attribute {*primaryKeyRoute}.
        /// asterisk(*) here is a wild-card/catch all i.e it matches the rest of the route after {entityName}.
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// MsSql: URL template: /<entityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: /Books </param>
        [HttpPut]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<IActionResult> Upsert(
            string entityName,
            string? primaryKeyRoute)
        {
            return await HandleOperation(
                entityName,
                Operation.Upsert,
                primaryKeyRoute);
        }

        /// <summary>
        /// Incremental Update/Insert action serving the HttpPatch verb
        /// </summary>
        /// <param name="entityName">The name of the entity.</param>
        /// <param name="primaryKeyRoute">The string representing the primary key route
        /// which gets its content from the route attribute {*primaryKeyRoute}.
        /// asterisk(*) here is a wild-card/catch all i.e it matches the rest of the route after {entityName}.
        /// primary_key = [shard_value/]id_key_value
        /// Expected URL template is of the following form:
        /// MsSql: URL template: /<entityName>/[<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: /Books </param>
        [HttpPatch]
        [Route("{*primaryKeyRoute}")]
        [Produces("application/json")]
        public async Task<IActionResult> UpsertIncremental(
            string entityName,
            string? primaryKeyRoute)
        {
            return await HandleOperation(
                entityName,
                Operation.UpsertIncremental,
                primaryKeyRoute);
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
            string? primaryKeyRoute)
        {
            try
            {
                // Parse App Service's EasyAuth injected headers into MiddleWare usable Security Principal
                ClaimsIdentity? identity = AppServiceAuthentication.Parse(this.HttpContext);
                if (identity != null)
                {
                    this.HttpContext.User = new ClaimsPrincipal(identity);
                }

                // Utilizes C#8 using syntax which does not require brackets.
                using JsonDocument? result
                    = await _restService.ExecuteAsync(
                            entityName,
                            operationType,
                            primaryKeyRoute);

                if (result != null)
                {
                    // Clones the root element to a new JsonElement that can be
                    // safely stored beyond the lifetime of the original JsonDocument.
                    JsonElement resultElement = result.RootElement.Clone();
                    OkObjectResult formattedResult = OkResponse(resultElement, entityName);

                    switch (operationType)
                    {
                        case Operation.Find:
                            return formattedResult;
                        case Operation.Insert:
                            primaryKeyRoute = _restService.ConstructPrimaryKeyRoute(entityName, resultElement);
                            string location =
                                UriHelper.GetEncodedUrl(HttpContext.Request) + "/" + primaryKeyRoute;
                            return new CreatedResult(location: location, formattedResult);
                        case Operation.Delete:
                            return new NoContentResult();
                        case Operation.Upsert:
                        case Operation.UpsertIncremental:
                            primaryKeyRoute = _restService.ConstructPrimaryKeyRoute(entityName, resultElement);
                            location =
                                UriHelper.GetEncodedUrl(HttpContext.Request) + "/" + primaryKeyRoute;
                            return new CreatedResult(location: location, formattedResult);
                        default:
                            throw new NotSupportedException($"Unsupported Operation: \" {operationType}\".");
                    }
                }
                else
                {
                    switch (operationType)
                    {
                        case Operation.Upsert:
                        case Operation.UpsertIncremental:
                            // Empty result set indicates an Update successfully occurred.
                            return new NoContentResult();
                        default:
                            throw new DataGatewayException(
                                message: $"Not Found",
                                statusCode: HttpStatusCode.NotFound,
                                subStatusCode: DataGatewayException.SubStatusCodes.EntityNotFound);
                    }
                }
            }
            catch (DataGatewayException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                Response.StatusCode = (int)ex.StatusCode;
                return ErrorResponse(ex.SubStatusCode.ToString(), ex.Message, ex.StatusCode);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return ErrorResponse(
                    DataGatewayException.SubStatusCodes.UnexpectedError.ToString(),
                    SERVER_ERROR,
                    HttpStatusCode.InternalServerError);
            }
        }
    }
}
