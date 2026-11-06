using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Field dashboard: latest telemetry (via HTTP GET function) + device registry (Azure SQL).</summary>
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly FunctionsClient _functions;
        private readonly SqlService _sql;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(FunctionsClient functions, SqlService sql, ILogger<DashboardController> logger)
        {
            _functions = functions;
            _sql = sql;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var vm = new DashboardViewModel();
            try
            {
                var (readings, source) = await _functions.GetTelemetryAsync(25);
                vm.Telemetry = readings;
                vm.TelemetrySource = source;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telemetry retrieval failed.");
                vm.TelemetrySource = "Unavailable";
                vm.Message = "Could not load telemetry: " + ex.Message;
                vm.MessageIsError = true;
            }

            if (_sql.IsConfigured)
            {
                try { vm.Devices = await _sql.GetDevicesAsync(); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Device registry query failed.");
                    vm.Message ??= "Could not load device registry: " + ex.Message;
                    vm.MessageIsError = true;
                }
            }
            return View(vm);
        }
    }
}
