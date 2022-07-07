using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    [Route("")]
    public sealed class HomeController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok();
        }
    }
}
