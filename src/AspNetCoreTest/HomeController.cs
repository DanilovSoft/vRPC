﻿namespace AspNetCoreTest
{
    using Microsoft.AspNetCore.Mvc;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
