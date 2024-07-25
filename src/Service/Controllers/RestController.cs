// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Controllers
{
    /// <summary>
    /// Controller to serve REST Api requests for the route /entityName.
    /// This controller should adhere to the
    /// <see href="https://github.com/Microsoft/api-guidelines/blob/vNext/Guidelines.md">Microsoft REST API Guidelines</see>.
    /// </summary>
    [ApiController]
    [Route("{*route}")]
    public class RestController : ControllerBase
    {
        private readonly RestService _restService;
        private readonly ILogger<RestController> _logger;

        public const string GENERIC_SERVER_ERROR = "While processing your request the server ran into an unexpected error.";
        public const string FAVICON_ROUTE = "favicon.ico";

        public RestController(RestService restService, ILogger<RestController> logger)
        {
            _restService = restService;
            _logger = logger;
        }

        /// <summary>
        /// Find action serving the HttpGet verb.
        /// </summary>
        /// <param name="route">The entire route which gets
        /// its content from the route attribute {*route} defined
        /// for the class, asterisk(*) here is a wild-card/catch all.
        /// Expected URL template is of the following form:
        /// MsSql/PgSql: URL template: <path>/<entityName>/<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: api/Books </param>
        [HttpGet]
        [Produces("application/json")]
        public async Task<IActionResult> Find(string route)
        {
            return await HandleOperation(route, EntityActionOperation.Read);
        }

        [HttpGet]
        [Route("/favicon.ico")]
        [Produces("application/json")]
        public IActionResult Favicon(string route)
        {
            return BadRequest();
        }

        /// <summary>
        /// Insert action serving the HttpPost verb.
        /// </summary>
        /// <param name="route">Path and entity.</param>
        /// Expected URL template is of the following form:
        /// CosmosDb/MsSql/PgSql: URL template: <path>/<entityName>
        /// URL MUST NOT contain a queryString
        /// URL example: api/SalesOrders </param>
        [HttpPost]
        [Produces("application/json")]
        public async Task<IActionResult> Insert(string route)
        {
            return await HandleOperation(route, EntityActionOperation.Insert);
        }

        /// <summary>
        /// Delete action serving the HttpDelete verb.
        /// </summary>
        /// <param name="route">The entire route which gets
        /// its content from the route attribute {*route} defined
        /// for the class, asterisk(*) here is a wild-card/catch all.
        /// Expected URL template is of the following form:
        /// MsSql/PgSql: URL template: <path>/<entityName>/<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: api/Books </param>
        [HttpDelete]
        [Produces("application/json")]
        public async Task<IActionResult> Delete(string route)
        {
            return await HandleOperation(route, EntityActionOperation.Delete);
        }

        /// <summary>
        /// Replacement Update/Insert action serving the HttpPut verb
        /// </summary>
        /// <param name="route">The entire route which gets
        /// its content from the route attribute {*route} defined
        /// for the class, asterisk(*) here is a wild-card/catch all.
        /// Expected URL template is of the following form:
        /// MsSql/PgSql: URL template: <path>/<entityName>/<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: api/Books </param>
        [HttpPut]
        [Produces("application/json")]
        public async Task<IActionResult> Upsert(string route)
        {
            return await HandleOperation(route, EntityActionOperation.Update);
        }

        /// <summary>
        /// Incremental Update/Insert action serving the HttpPatch verb
        /// </summary>
        /// <param name="route">The entire route which gets
        /// its content from the route attribute {*route} defined
        /// for the class, asterisk(*) here is a wild-card/catch all.
        /// Expected URL template is of the following form:
        /// MsSql/PgSql: URL template: <path>/<entityName>/<primary_key_column_name>/<primary_key_value>
        /// URL MUST NOT contain a queryString
        /// URL example: api/Books </param>
        [HttpPatch]
        [Produces("application/json")]
        public async Task<IActionResult> UpsertIncremental(string route)
        {
            return await HandleOperation(route, EntityActionOperation.UpdateIncremental);
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
        /// Handle the given operation.
        /// </summary>
        /// <param name="route">The entire route.</param>
        /// <param name="operationType">The kind of operation to handle.</param>
        private async Task<IActionResult> HandleOperation(
            string route,
            EntityActionOperation operationType)
        {
            try
            {
                // Validate the PathBase matches the configured REST path.
                string routeAfterPathBase = _restService.GetRouteAfterPathBase(route);

                (string entityName, string primaryKeyRoute) = _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(routeAfterPathBase);

                IActionResult? result = await _restService.ExecuteAsync(entityName, operationType, primaryKeyRoute);

                if (result is null)
                {
                    throw new DataApiBuilderException(
                        message: $"Not Found",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }

                return result;
            }
            catch (DataApiBuilderException ex)
            {
                _logger.LogError(
                    exception: ex,
                    message: "{correlationId} Error handling REST request.",
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContext));

                Response.StatusCode = (int)ex.StatusCode;
                return ErrorResponse(ex.SubStatusCode.ToString(), ex.Message, ex.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    exception: ex,
                    message: "{correlationId} Internal server error occured during REST request processing.",
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContext));

                Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                return ErrorResponse(
                    DataApiBuilderException.SubStatusCodes.UnexpectedError.ToString(),
                    GENERIC_SERVER_ERROR,
                    HttpStatusCode.InternalServerError);
            }
        }
    }
}
