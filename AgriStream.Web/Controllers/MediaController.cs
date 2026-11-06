using AgriStream.Web.Models;
using AgriStream.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgriStream.Web.Controllers
{
    /// <summary>Drone imagery: uploads land in Blob Storage (firing the blob-triggered function);
    /// the audit rows it writes to Table Storage are shown alongside the blob list.</summary>
    [Authorize]
    public class MediaController : Controller
    {
        private readonly StorageService _storage;
        private readonly ILogger<MediaController> _logger;

        public MediaController(StorageService storage, ILogger<MediaController> logger)
        {
            _storage = storage;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            var vm = new MediaViewModel();
            if (!_storage.IsConfigured)
            {
                TempData["Error"] = "Azure Storage is not configured. Set Azure:StorageConnectionString.";
                return View(vm);
            }
            try
            {
                vm.Images = await _storage.ListImagesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Listing blobs failed.");
                TempData["Error"] = "Could not list blob images: " + ex.Message;
            }
            try
            {
                vm.AuditEntries = await _storage.GetAuditEntriesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Listing audit entries failed.");
            }
            return View(vm);
        }

        [HttpGet]
        public IActionResult Upload() => View();

        [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(52_428_800)]
        public async Task<IActionResult> Upload(IFormFile? image)
        {
            if (image is null || image.Length == 0)
            {
                ModelState.AddModelError("", "Choose an image file to upload.");
                return View();
            }
            if (!_storage.IsConfigured)
            {
                ModelState.AddModelError("", "Azure Storage is not configured.");
                return View();
            }

            try
            {
                var name = await _storage.UploadImageAsync(image, User.Identity?.Name ?? "unknown");
                TempData["Message"] = $"Uploaded '{image.FileName}' as blob '{name}'. The blob-triggered function will write an audit row shortly.";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob upload failed.");
                ModelState.AddModelError("", "Upload failed: " + ex.Message);
                return View();
            }
        }
    }
}
