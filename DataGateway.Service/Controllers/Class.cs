namespace Azure.DataGateway.Service.Controllers
{
    using Microsoft.AspNetCore.Mvc;

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
