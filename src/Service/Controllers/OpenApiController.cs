// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers
{
    /// <summary>
    /// 
    /// </summary>
    [Route("api/[controller]")]
    [ApiController]
    public class OpenApiController : ControllerBase
    {
        private IOpenApiDocumentor _apiDocumentor;

        public OpenApiController(IOpenApiDocumentor openApiDocumentor)
        {
            _apiDocumentor = openApiDocumentor;
            Console.WriteLine("api controller constructor");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public IActionResult Get()
        {
            return _apiDocumentor.TryGetDocument(out string? document) ? Ok(document) : NotFound();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        [HttpPost]
        public IActionResult Post()
        {
            try
            {
                _apiDocumentor.CreateDocument();

                if (_apiDocumentor.TryGetDocument(out string? document))
                {
                    return new CreatedResult(location:"/openapi" ,value: document);
                }

                return NotFound();
            }
            catch (Exception)
            {
                return new StatusCodeResult(statusCode: (int)HttpStatusCode.InternalServerError);
            }
        }
    }
}
