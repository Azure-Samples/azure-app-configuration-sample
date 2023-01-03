using Microsoft.AspNetCore.Mvc;
namespace WebAPI.Controllers
{
    [ApiExplorerSettings(IgnoreApi = true)]
    public class resourceController : Controller
    {
        [Route("/")]
        [Route("/docs")]
        [Route("/swagger")]
        public IActionResult Index()
        {
            return new RedirectResult("~/swagger");
        }
    }
}