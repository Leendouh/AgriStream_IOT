using System.Security.Claims;
using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Cookie-based login backed by the Azure SQL Users table.</summary>
    public class AccountController : Controller
    {
        private readonly SqlService _sql;
        private readonly ILogger<AccountController> _logger;

        public AccountController(SqlService sql, ILogger<AccountController> logger)
        {
            _sql = sql;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            if (!ModelState.IsValid) return View(model);
            if (!_sql.IsConfigured)
            {
                ModelState.AddModelError("", "Azure SQL is not configured. Set Azure:SqlConnectionString.");
                return View(model);
            }

            AppUser? user;
            try
            {
                user = await _sql.ValidateUserAsync(model.Username, model.Password);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SQL login validation failed.");
                ModelState.AddModelError("", "Could not reach the database: " + ex.Message);
                return View(model);
            }
            if (user is null)
            {
                ModelState.AddModelError("", "Invalid username or password.");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            _logger.LogInformation("User {User} logged in with role {Role}.", user.Username, user.Role);
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);
            return RedirectToAction("Index", "Dashboard");
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }

        public IActionResult AccessDenied() => View();
    }
}
