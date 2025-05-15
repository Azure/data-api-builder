// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Azure.DataApiBuilder.Service.Controllers
{
    /// <summary>
    /// This controller corresponds to the /health endpoint of DAB
    /// </summary>
    [ApiController]
    [Route("/health")]
    public class HealthController : ControllerBase
    {
        public IHttpContextAccessor IHttpContextAccessor;
        public ComprehensiveHealthReportResponseWriter ComprehensiveHealthReportResponseWriter;

        /// <summary>
        /// The constructor for the HealthController
        /// </summary>
        /// <param name="contextAccessor">IHttpContextAccessor to fetch the http context.</param>
        /// <param name="comprehensiveHealthReportResponseWriter">ComprehensiveHealthReportResponseWriter to get the report for health.</param>
        public HealthController(IHttpContextAccessor contextAccessor, ComprehensiveHealthReportResponseWriter comprehensiveHealthReportResponseWriter)
        {
            IHttpContextAccessor = contextAccessor;
            ComprehensiveHealthReportResponseWriter = comprehensiveHealthReportResponseWriter;
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        /// <returns>Returns the ComprehensiveHealthReportResponse to be displayed at /health endpoint</returns>
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task Get()
        {
            if (IHttpContextAccessor != null && IHttpContextAccessor.HttpContext != null)
            {
                await ComprehensiveHealthReportResponseWriter.WriteResponseAsync(IHttpContextAccessor.HttpContext);
            }

            return;
        }
    }
}
