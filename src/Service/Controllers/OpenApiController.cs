// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers
{
    /// <summary>
    /// 
    /// </summary>
    [Route("[controller]")]
    [ApiController]
    public class OpenApiController : ControllerBase
    {
        private IOpenApiDocumentor _apiDocumentor;

        public OpenApiController(IOpenApiDocumentor openApiDocumentor)
        {
            _apiDocumentor = openApiDocumentor;
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
