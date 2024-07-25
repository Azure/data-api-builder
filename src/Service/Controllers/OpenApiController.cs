// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net.Mime;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers;

[Route("{value}/openapi")]
[ApiController]
public class OpenApiController : ControllerBase
{
    private IOpenApiDocumentor _openApiDocumentor;

    public OpenApiController(IOpenApiDocumentor openApiDocumentor)
    {
        _openApiDocumentor = openApiDocumentor;
    }

    // GET: api/<OpenApiController>
    [HttpGet]
    public ActionResult Get()
    {
        if (_openApiDocumentor.TryGetDocument(out string? document))
        {
            return Content(document, MediaTypeNames.Application.Json);
        }

        return NotFound();
    }
}
