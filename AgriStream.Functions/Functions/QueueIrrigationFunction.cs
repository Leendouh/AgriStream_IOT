using System.Net;
using System.Text.Json;
using AgriStream.Functions.Support;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions
{
    /// <summary>
    /// HTTP POST TRIGGER - accepts an irrigation command from the MVC app (or a sensor),
    /// validates it and places the message on the "irrigation-commands" Storage Queue.
    /// Route: POST /api/irrigation   Body: { "deviceId": "PUMP-01", "durationSeconds": 60, "requestedBy": "farmer" }
    /// </summary>
    public class QueueIrrigationFunction
    {
        private readonly ILogger<QueueIrrigationFunction> _logger;

        public QueueIrrigationFunction(ILogger<QueueIrrigationFunction> logger) => _logger = logger;

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        [Function(nameof(QueueIrrigationFunction))]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = "irrigation")] HttpRequestData req)
        {
            var body = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation("Irrigation request body ({Len} chars): {Body}", body?.Length ?? 0, body);

            var command = string.IsNullOrWhiteSpace(body) ? null
                : JsonSerializer.Deserialize<IrrigationCommand>(body, JsonOpts);

            if (command is null || string.IsNullOrWhiteSpace(command.DeviceId))
            {
                var bad = req.CreateResponse(HttpStatusCode.BadRequest);
                await bad.WriteAsJsonAsync(new { error = "Body must contain a deviceId." });
                return bad;
            }

            command.DeviceId = command.DeviceId.Trim().ToUpperInvariant();
            command.DurationSeconds = Math.Clamp(command.DurationSeconds, 5, 3600);
            if (command.IssuedAt == default) command.IssuedAt = DateTimeOffset.UtcNow;

            var queue = Env.Queue();
            await queue.CreateIfNotExistsAsync();
            var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await queue.SendMessageAsync(json);

            _logger.LogInformation("Queued irrigation command for {Device} ({Seconds}s, by {User}).",
                command.DeviceId, command.DurationSeconds, command.RequestedBy);

            var res = req.CreateResponse(HttpStatusCode.Accepted);
            await res.WriteAsJsonAsync(new { status = "queued", command.DeviceId, command.DurationSeconds });
            return res;
        }
    }
}
