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

        /// <summary>
        /// Trigger the creation of the OpenAPI description document if it wasn't already created
        /// using this method or created during engine startup.
        /// </summary>
        /// <returns>
        /// HTTP 201 - OpenAPI description document if creation was triggered
        /// HTTP 405 - Document creation method not allowed, global REST endpoint disabled in runtime config.
        /// HTTP 409 - Document already created
        /// HTTP 500 - Document creation failed. </returns>
        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public IActionResult Post()
        {
            try
            {
                _apiDocumentor.CreateDocument();

                if (_apiDocumentor.TryGetDocument(out string? document))
                {
                    return new CreatedResult(location: "/openapi", value: document);
                }

                return NotFound();
            }
            catch (DataApiBuilderException dabException)
            {
                Response.StatusCode = (int)dabException.StatusCode;
                return new JsonResult(new
                {
                    error = new
                    {
                        code = dabException.SubStatusCode.ToString(),
                        message = dabException.Message,
                        status = (int)dabException.StatusCode
                    }
                });
            }
        }
    }
}
