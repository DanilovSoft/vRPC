using Microsoft.AspNetCore.Mvc;

namespace AspNetCoreTest
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
