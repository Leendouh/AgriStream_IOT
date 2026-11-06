using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Telemetry ingest + listing. The POST form simulates sensor devices writing
    /// high-frequency readings into Azure Table Storage.</summary>
    [Authorize]
    public class TelemetryController : Controller
    {
        private readonly StorageService _storage;
        private readonly ILogger<TelemetryController> _logger;

        public TelemetryController(StorageService storage, ILogger<TelemetryController> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var vm = new TelemetryViewModel();
            if (_storage.IsConfigured)
            {
                try { vm.Readings = await _storage.GetLatestTelemetryAsync(50); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telemetry query failed.");
                    TempData["Error"] = "Could not read telemetry table: " + ex.Message;
                }
            }
            else
            {
                TempData["Error"] = "Azure Storage is not configured. Set Azure:StorageConnectionString.";
            }
            return View(vm);
        }

        /// <summary>Simulates a sensor pushing a reading into Table Storage.</summary>
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Ingest(TelemetryViewModel model)
        {
            if (!ModelState.IsValid) return RedirectToAction(nameof(Index));
            if (!_storage.IsConfigured)
            {
                TempData["Error"] = "Azure Storage is not configured.";
                return RedirectToAction(nameof(Index));
            }

            await _storage.AddTelemetryAsync(new TelemetryReading
            {
                DeviceId = model.DeviceId.Trim().ToUpperInvariant(),
                Timestamp = DateTimeOffset.UtcNow,
                SoilMoisture = model.SoilMoisture,
                TemperatureC = model.TemperatureC,
                Humidity = model.Humidity,
                BatteryLevel = model.BatteryLevel,
            });
            TempData["Message"] = $"Telemetry from {model.DeviceId} written to Azure Table Storage.";
            return RedirectToAction(nameof(Index));
        }

        /// <summary>Anonymous JSON ingest endpoint for sensor simulators / load scripts.</summary>
        [HttpPost("/api/telemetry"), AllowAnonymous, IgnoreAntiforgeryToken]
        public async Task<IActionResult> IngestApi([FromBody] TelemetryReading reading)
        {
            if (!_storage.IsConfigured) return StatusCode(503, new { error = "Storage not configured" });
            if (string.IsNullOrWhiteSpace(reading.DeviceId)) return BadRequest(new { error = "DeviceId required" });

            reading.DeviceId = reading.DeviceId.Trim().ToUpperInvariant();
            if (reading.Timestamp == default) reading.Timestamp = DateTimeOffset.UtcNow;
            await _storage.AddTelemetryAsync(reading);
            return Accepted(new { status = "stored", reading.DeviceId });
        }
    }
}
