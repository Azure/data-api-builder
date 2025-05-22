// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
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
        /// <summary>
        /// Service providing REST Api executions.
        /// </summary>
        private readonly RestService _restService;

        /// <summary>
        /// OpenAPI description document creation service.
        /// </summary>
        private readonly IOpenApiDocumentor _openApiDocumentor;
        /// <summary>
        /// String representing the value associated with "code" for a server error
        /// </summary>
        public const string SERVER_ERROR = "While processing your request the server ran into an unexpected error.";

        /// <summary>
        /// Every GraphQL request gets redirected to this route
        /// when Banana Cake Pop UI is disabled.
        /// e.g. https://servername:port/favicon.ico
        /// </summary>
        public const string REDIRECTED_ROUTE = "favicon.ico";

        private readonly ILogger<RestController> _logger;

        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// Constructor.
        /// </summary>
        public RestController(RuntimeConfigProvider runtimeConfigProvider, RestService restService, IOpenApiDocumentor openApiDocumentor, ILogger<RestController> logger)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _restService = restService;
            _openApiDocumentor = openApiDocumentor;
            _logger = logger;
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
        public async Task<IActionResult> Find(
            string route)
        {
            return await HandleOperation(
                route,
                EntityActionOperation.Read);
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
        public async Task<IActionResult> Insert(
            string route)
        {
            return await HandleOperation(
                route,
                EntityActionOperation.Insert);
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
        public async Task<IActionResult> Delete(
            string route)
        {
            return await HandleOperation(
                route,
                EntityActionOperation.Delete);
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
        public async Task<IActionResult> Upsert(
            string route)
        {
            return await HandleOperation(
                route,
                DeterminePatchPutSemantics(EntityActionOperation.Upsert));
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
        public async Task<IActionResult> UpsertIncremental(
            string route)
        {
            return await HandleOperation(
                route,
                DeterminePatchPutSemantics(EntityActionOperation.UpsertIncremental));
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
            if (route.Equals(REDIRECTED_ROUTE))
            {
                return NotFound();
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            // This activity tracks the entire REST request.
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity($"{HttpContext.Request.Method} {(route.Split('/').Length > 1 ? route.Split('/')[1] : string.Empty)}");

            try
            {
                TelemetryMetricsHelper.IncrementActiveRequests(ApiType.REST);

                if (activity is not null)
                {
                    activity.TrackMainControllerActivityStarted(
                        Enum.Parse<HttpMethod>(HttpContext.Request.Method, ignoreCase: true),
                        HttpContext.Request.Headers["User-Agent"].ToString(),
                        operationType.ToString(),
                        route,
                        HttpContext.Request.QueryString.ToString(),
                        HttpContext.Request.Headers["X-MS-API-ROLE"].FirstOrDefault() ?? HttpContext.User.FindFirst("role")?.Value,
                        ApiType.REST);
                }

                // Validate the PathBase matches the configured REST path.
                string routeAfterPathBase = _restService.GetRouteAfterPathBase(route);

                // Explicitly handle OpenAPI description document retrieval requests.
                if (string.Equals(routeAfterPathBase, OpenApiDocumentor.OPENAPI_ROUTE, StringComparison.OrdinalIgnoreCase))
                {
                    if (_openApiDocumentor.TryGetDocument(out string? document))
                    {
                        return Content(document, MediaTypeNames.Application.Json);
                    }

                    return NotFound();
                }

                (string entityName, string primaryKeyRoute) = _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(routeAfterPathBase);

                // This activity tracks the query execution. This will create a new activity nested under the REST request activity.
                using Activity? queryActivity = TelemetryTracesHelper.DABActivitySource.StartActivity($"QUERY {entityName}");
                IActionResult? result = await _restService.ExecuteAsync(entityName, operationType, primaryKeyRoute);

                RuntimeConfig runtimeConfig = _runtimeConfigProvider.GetConfig();
                string dataSourceName = runtimeConfig.GetDataSourceNameFromEntityName(entityName);
                DatabaseType databaseType = runtimeConfig.GetDataSourceFromDataSourceName(dataSourceName).DatabaseType;

                if (queryActivity is not null)
                {
                    queryActivity.TrackQueryActivityStarted(
                        databaseType,
                        dataSourceName);
                }

                if (result is null)
                {
                    throw new DataApiBuilderException(
                        message: $"Not Found",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }

                int statusCode = (result as ObjectResult)?.StatusCode ?? (result as StatusCodeResult)?.StatusCode ?? (result as JsonResult)?.StatusCode ?? 200;
                if (activity is not null && activity.IsAllDataRequested)
                {
                    HttpStatusCode httpStatusCode = Enum.Parse<HttpStatusCode>(statusCode.ToString(), ignoreCase: true);
                    activity.TrackMainControllerActivityFinished(httpStatusCode);
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
                activity?.TrackMainControllerActivityFinishedWithException(ex, ex.StatusCode);

                HttpMethod method = Enum.Parse<HttpMethod>(HttpContext.Request.Method, ignoreCase: true);
                TelemetryMetricsHelper.TrackError(method, ex.StatusCode, route, ApiType.REST, ex);
                return ErrorResponse(ex.SubStatusCode.ToString(), ex.Message, ex.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    exception: ex,
                    message: "{correlationId} Internal server error occured during REST request processing.",
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContext));

                Response.StatusCode = (int)HttpStatusCode.InternalServerError;

                HttpMethod method = Enum.Parse<HttpMethod>(HttpContext.Request.Method, ignoreCase: true);
                activity?.TrackMainControllerActivityFinishedWithException(ex, HttpStatusCode.InternalServerError);

                TelemetryMetricsHelper.TrackError(method, HttpStatusCode.InternalServerError, route, ApiType.REST, ex);
                return ErrorResponse(
                    DataApiBuilderException.SubStatusCodes.UnexpectedError.ToString(),
                    SERVER_ERROR,
                    HttpStatusCode.InternalServerError);
            }
            finally
            {
                stopwatch.Stop();
                HttpMethod method = Enum.Parse<HttpMethod>(HttpContext.Request.Method, ignoreCase: true);
                HttpStatusCode httpStatusCode = Enum.Parse<HttpStatusCode>(Response.StatusCode.ToString(), ignoreCase: true);
                TelemetryMetricsHelper.TrackRequest(method, httpStatusCode, route, ApiType.REST);
                TelemetryMetricsHelper.TrackRequestDuration(method, httpStatusCode, route, ApiType.REST, stopwatch.Elapsed);

                TelemetryMetricsHelper.DecrementActiveRequests(ApiType.REST);
            }
        }

        /// <summary>
        /// Helper function determines the correct operation based on the client
        /// provided headers. Client can indicate if operation should follow
        /// update or upsert semantics.
        /// </summary>
        /// <param name="operation">opertion to be used.</param>
        /// <returns>correct opertion based on headers.</returns>
        private EntityActionOperation DeterminePatchPutSemantics(EntityActionOperation operation)
        {

            if (HttpContext.Request.Headers.ContainsKey("If-Match"))
            {
                if (!string.Equals(HttpContext.Request.Headers["If-Match"], "*"))
                {
                    throw new DataApiBuilderException(
                        message: "Etags not supported, use '*'",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.BadRequest);
                }

                switch (operation)
                {
                    case EntityActionOperation.Upsert:
                        operation = EntityActionOperation.Update;
                        break;
                    case EntityActionOperation.UpsertIncremental:
                        operation = EntityActionOperation.UpdateIncremental;
                        break;
                }
            }

            return operation;
        }
    }
}
