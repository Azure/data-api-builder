using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        private OkObjectResult OkResponse(JsonElement jsonResult)
        {
            // For consistency we return all values as type Array
            if (jsonResult.ValueKind != JsonValueKind.Array)
            {
                string jsonString = $"[{JsonSerializer.Serialize(jsonResult)}]";
                jsonResult = JsonSerializer.Deserialize<JsonElement>(jsonString);
            }

            IEnumerable<JsonElement> resultEnumerated = jsonResult.EnumerateArray();
            // More than 0 records, and the last element is of type array, then we have pagination
            if (resultEnumerated.Count() > 0 && resultEnumerated.Last().ValueKind == JsonValueKind.Array)
            {
                // Get the nextLink
                // resultEnumerated will be an array of the form
                // [{object1}, {object2},...{objectlimit}, [{nextLinkObject}]]
                // if the last element is of type array, we know it is nextLink
                // we strip the "[" and "]" and then save the nextLink element
                // into a dictionary with a key of "nextLink" and a value that
                // represents the nextLink data we require.
                string nextLinkJsonString = JsonSerializer.Serialize(resultEnumerated.Last());
                Dictionary<string, object> nextLink = JsonSerializer.Deserialize<Dictionary<string, object>>(nextLinkJsonString[1..^1])!;
                IEnumerable<JsonElement> value = resultEnumerated.Take(resultEnumerated.Count() - 1);
                return Ok(new
                {
                    value = value,
                    @nextLink = nextLink["nextLink"]
                });

            }

            // no pagination, do not need nextLink
            return Ok(new
            {
                value = resultEnumerated
            });
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
                    OkObjectResult formattedResult = OkResponse(resultElement);

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
