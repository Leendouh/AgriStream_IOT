using System.Net;
using System.Web;
using AgriStream.Functions.Support;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions
{
    /// <summary>
    /// HTTP GET TRIGGER - returns the newest telemetry rows from Azure Table Storage
    /// as JSON so the MVC dashboard can render near-instantly.
    /// Route: GET /api/telemetry?count=25&deviceId=SENSOR-A1
    /// </summary>
    public class GetTelemetryFunction
    {
        private readonly ILogger<GetTelemetryFunction> _logger;

        public GetTelemetryFunction(ILogger<GetTelemetryFunction> logger) => _logger = logger;

        [Function(nameof(GetTelemetryFunction))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "telemetry")] HttpRequestData req)
        {
            var query = HttpUtility.ParseQueryString(req.Url.Query);
            var count = int.TryParse(query["count"], out var c) ? Math.Clamp(c, 1, 500) : 25;
            var deviceId = query["deviceId"];

            var table = Env.Table(Env.TelemetryTable);
            await table.CreateIfNotExistsAsync();

            // RowKeys are inverted ticks, so rows arrive newest-first per partition.
            string? filter = string.IsNullOrWhiteSpace(deviceId)
                ? null
                : $"PartitionKey eq '{deviceId.Replace("'", "''")}'";

            var readings = new List<TelemetryReading>();
            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter, maxPerPage: count))
            {
                readings.Add(new TelemetryReading
                {
                    DeviceId = e.GetString("DeviceId") ?? e.PartitionKey,
                    Timestamp = e.GetDateTimeOffset("Timestamp") ?? e.Timestamp ?? DateTimeOffset.MinValue,
                    SoilMoisture = e.GetDouble("SoilMoisture") ?? 0,
                    TemperatureC = e.GetDouble("TemperatureC") ?? 0,
                    Humidity = e.GetDouble("Humidity") ?? 0,
                    BatteryLevel = e.GetDouble("BatteryLevel") ?? 0,
                });
                if (readings.Count >= count) break;
            }

            _logger.LogInformation("GetTelemetry returned {Count} rows (deviceId={Device}).", readings.Count, deviceId ?? "*");

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteAsJsonAsync(readings);
            return res;
        }
    }
}
