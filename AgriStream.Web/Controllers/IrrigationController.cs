using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Sends "Trigger Irrigation" commands through the HTTP POST function onto the queue.</summary>
    [Authorize]
    public class IrrigationController : Controller
    {
        private readonly FunctionsClient _functions;

        public IrrigationController(FunctionsClient functions) => _functions = functions;

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Trigger(string deviceId, int durationSeconds = 60)
        {
            var command = new IrrigationCommand
            {
                DeviceId = deviceId,
                DurationSeconds = Math.Clamp(durationSeconds, 5, 3600),
                RequestedBy = User.Identity?.Name ?? "unknown",
                IssuedAt = DateTimeOffset.UtcNow,
            };

            try
            {
                TempData["Message"] = await _functions.QueueIrrigationAsync(command);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Could not queue irrigation command: " + ex.Message;
            }
            return RedirectToAction("Index", "Dashboard");
        }
    }
}
