using Microsoft.AspNetCore.Mvc;

namespace Azure.DataGateway.Service.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return Ok();
        }
    }
}
