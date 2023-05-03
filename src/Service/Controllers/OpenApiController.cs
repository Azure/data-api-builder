// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Mime;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services.OpenAPI;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers
{
    /// <summary>
    /// Facilitate access to a created OpenAPI description document or trigger the creation of
    /// the OpenAPI description document.
    /// </summary>
    [Route("[controller]")]
    [ApiController]
    public class OpenApiController : ControllerBase
    {
        /// <summary>
        /// OpenAPI description document creation service.
        /// </summary>
        private readonly IOpenApiDocumentor _apiDocumentor;

        public OpenApiController(IOpenApiDocumentor openApiDocumentor)
        {
            _apiDocumentor = openApiDocumentor;
        }

        /// <summary>
        /// Get the created OpenAPI description document created to represent the possible
        /// paths and operations on the DAB engine's REST endpoint.
        /// </summary>
        /// <returns>
        /// HTTP 200 - Open API description document.
        /// HTTP 404 - OpenAPI description document not available since it hasn't been created
        /// or failed to be created.</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult Get()
        {
            if (_apiDocumentor.TryGetDocument(out string? document))
            {
                return Content(document, MediaTypeNames.Application.Json);
            }

            return NotFound();
        }
    }
}
