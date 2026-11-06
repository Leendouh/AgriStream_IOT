using System.Text;
using System.Text.Json;
using AgriStream.Functions.Support;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions
{
    /// <summary>
    /// QUEUE TRIGGER - dequeues irrigation commands, updates the device's operational
    /// status in the Azure SQL DeviceRegistry master table, and appends an event row
    /// to the IrrigationEvents table.
    /// </summary>
    public class ProcessIrrigationFunction
    {
        private readonly ILogger<ProcessIrrigationFunction> _logger;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public ProcessIrrigationFunction(ILogger<ProcessIrrigationFunction> logger) => _logger = logger;

        [Function(nameof(ProcessIrrigationFunction))]
        public async Task Run(
            [QueueTrigger("irrigation-queue", Connection = "AzureWebJobsStorage")] string message)
        {
            var command = ParseMessage(message);
            if (command is null)
            {
                _logger.LogWarning("Discarding unparseable queue message: {Message}", message);
                return;
            }

            _logger.LogInformation("Processing irrigation for {Device} ({Seconds}s, by {User}).",
                command.DeviceId, command.DurationSeconds, command.RequestedBy);

            // 1. Update the master device registry in Azure SQL.
            await using (var conn = Env.Sql())
            {
                await conn.OpenAsync();
                await using var cmd = new SqlCommand("""
                    UPDATE dbo.DeviceRegistry
                    SET Status = 'Irrigating', LastCommandAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME()
                    WHERE DeviceId = @id
                    """, conn);
                cmd.Parameters.AddWithValue("@id", command.DeviceId);
                var rows = await cmd.ExecuteNonQueryAsync();
                if (rows == 0)
                    _logger.LogWarning("Device {Device} not found in DeviceRegistry.", command.DeviceId);
            }

            // 2. Append an audit/event row to Table Storage.
            var table = Env.Table(Env.EventsTable);
            await table.CreateIfNotExistsAsync();
            var entity = new TableEntity(command.DeviceId,
                (DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UtcNow.Ticks).ToString("D19"))
            {
                ["DeviceId"] = command.DeviceId,
                ["Event"] = "IrrigationStarted",
                ["DurationSeconds"] = command.DurationSeconds,
                ["RequestedBy"] = command.RequestedBy,
                ["ProcessedAt"] = DateTimeOffset.UtcNow,
            };
            await table.AddEntityAsync(entity);

            _logger.LogInformation("Device {Device} marked Irrigating; event logged.", command.DeviceId);
        }

        /// <summary>Handles both plain JSON and base64-encoded queue payloads.</summary>
        private static IrrigationCommand? ParseMessage(string message)
        {
            foreach (var candidate in new[] { message, TryBase64Decode(message) })
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    var cmd = JsonSerializer.Deserialize<IrrigationCommand>(candidate, JsonOpts);
                    if (!string.IsNullOrWhiteSpace(cmd?.DeviceId)) return cmd;
                }
                catch (JsonException) { }
            }
            return null;
        }

        private static string? TryBase64Decode(string s)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch (FormatException) { return null; }
        }
    }
}
