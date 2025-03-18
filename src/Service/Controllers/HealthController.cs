// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers
{
    [ApiController]
    [Route("/health")]
    public class HealthController : ControllerBase
    {
        public IHttpContextAccessor httpContextAccessor;
        public ComprehensiveHealthReportResponseWriter comprehensiveHealthReportResponseWriter;

        public HealthController(IHttpContextAccessor contextAccessor, ComprehensiveHealthReportResponseWriter comprehensiveHealthReportResponseWriter)
        {
            httpContextAccessor = contextAccessor;
            this.comprehensiveHealthReportResponseWriter = comprehensiveHealthReportResponseWriter;
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Returns</returns>
        [HttpGet]
        public async Task Get()
        {
            if (httpContextAccessor != null && httpContextAccessor.HttpContext != null)
            {
                await comprehensiveHealthReportResponseWriter.WriteResponse(httpContextAccessor.HttpContext);
            }

            return;
        }
    }
}
