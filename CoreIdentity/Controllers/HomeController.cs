using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using CoreIdentity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace CoreIdentity.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        SignInManager<CustomerUser> _SignInManager;
        UserManager<CustomerUser> _userManager;
        public HomeController(ILogger<HomeController> logger, SignInManager<CustomerUser> signInManager, UserManager<CustomerUser> userManager)
        {
            _logger = logger;
            _SignInManager = signInManager;
            _userManager = userManager;
        }

        public IActionResult Index()
        {
            return View();
        }

        [Authorize]
        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(CustomerUser customer, [FromQuery]string returnurl = null)
        {
            var user = await _userManager.FindByNameAsync(customer.UserName);
            if (user == null)
            {
                user = new CustomerUser
                {
                    UserName = customer.UserName,
                    Age = 18,
                    PhoneNumber = "12321312312",
                    OpenId = "abc",
                    Email = "abd@dd.com",
                    EmailConfirmed = true
                };
                var result = await _userManager.CreateAsync(user, "12345W6dfs@1");
                if (!result.Succeeded)
                {
                    var aa = result.Errors;
                }


            }
            var result1 = await _SignInManager.PasswordSignInAsync(customer.UserName, "12345W6dfs@1", false, false);
            if (result1.Succeeded)
            {
                if (returnurl != null)
                {
                    return Redirect(returnurl);
                }
            }
            else if (result1.IsNotAllowed)
            { 
            }

            return RedirectToAction("index");
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await _SignInManager.SignOutAsync();
            return RedirectToAction("index");
        }
    }
}
