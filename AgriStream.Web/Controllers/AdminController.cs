using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Admin-only view over the SQL Users table and DeviceRegistry.</summary>
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly SqlService _sql;

        public AdminController(SqlService sql) => _sql = sql;

        public async Task<IActionResult> Index()
        {
            var vm = new AdminViewModel();
            if (!_sql.IsConfigured)
            {
                TempData["Error"] = "Azure SQL is not configured. Set Azure:SqlConnectionString.";
                return View(vm);
            }
            vm.Users = await _sql.GetUsersAsync();
            vm.Devices = await _sql.GetDevicesAsync();
            return View(vm);
        }
    }
}
