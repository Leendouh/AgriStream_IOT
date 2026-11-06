using System.Net.Http.Json;
using System.Text.Json;
using AgriStream.Web.Models;
using AgriStream.Web.Options;
using Microsoft.Extensions.Options;

namespace AgriStream.Web.Services
{
    /// <summary>
    /// Calls the two HTTP-triggered Azure Functions. If a function URL is not configured,
    /// falls back to direct Storage access so the app still works before the functions deploy.
    /// </summary>
    public class FunctionsClient
    {
        private readonly HttpClient _http;
        private readonly AzureOptions _opts;
        private readonly StorageService _storage;
        private readonly ILogger<FunctionsClient> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public FunctionsClient(HttpClient http, IOptions<AzureOptions> opts, StorageService storage, ILogger<FunctionsClient> logger)
        {
            _http = http;
            _opts = opts.Value;
            _storage = storage;
            _logger = logger;
        }

        /// <summary>GET latest telemetry via the HTTP GET function (or direct Table read as fallback).</summary>
        public async Task<(List<TelemetryReading> Readings, string Source)> GetTelemetryAsync(int count = 25, string? deviceId = null)
        {
            if (!string.IsNullOrWhiteSpace(_opts.TelemetryFunctionUrl))
            {
                try
                {
                    var url = _opts.TelemetryFunctionUrl +
                              (_opts.TelemetryFunctionUrl.Contains('?') ? "&" : "?") + $"count={count}";
                    if (!string.IsNullOrWhiteSpace(deviceId))
                        url += $"&deviceId={Uri.EscapeDataString(deviceId)}";

                    var readings = await _http.GetFromJsonAsync<List<TelemetryReading>>(url, JsonOpts);
                    return (readings ?? new(), "HTTP GET Function");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Telemetry function call failed; falling back to direct Table read.");
                }
            }
            return (await _storage.GetLatestTelemetryAsync(count, deviceId), "Direct Table Storage read");
        }

        /// <summary>POST an irrigation command via the HTTP POST function (or enqueue directly as fallback).</summary>
        public async Task<string> QueueIrrigationAsync(IrrigationCommand command)
        {
            if (!string.IsNullOrWhiteSpace(_opts.IrrigationFunctionUrl))
            {
                try
                {
                    var res = await _http.PostAsJsonAsync(_opts.IrrigationFunctionUrl, command, JsonOpts);
                    if (res.IsSuccessStatusCode)
                        return "Irrigation command queued via HTTP POST Function.";
                    _logger.LogWarning("Irrigation function returned {Status}; falling back to direct enqueue.", res.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Irrigation function call failed; falling back to direct enqueue.");
                }
            }
            await _storage.EnqueueIrrigationAsync(command);
            return "Irrigation command queued directly to Storage Queue.";
        }
    }
}
