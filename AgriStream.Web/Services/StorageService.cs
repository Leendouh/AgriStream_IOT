using System.Text;
using System.Text.Json;
using AgriStream.Web.Models;
using AgriStream.Web.Options;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Queues;
using Microsoft.Extensions.Options;

namespace AgriStream.Web.Services
{
    /// <summary>
    /// Wraps Azure Storage: Blob (drone imagery), Table (telemetry + audit logs)
    /// and Queue (irrigation commands fallback path).
    /// </summary>
    public class StorageService
    {
        private readonly AzureOptions _opts;
        private readonly ILogger<StorageService> _logger;

        public StorageService(IOptions<AzureOptions> opts, ILogger<StorageService> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_opts.StorageConnectionString);

        private BlobContainerClient Container =>
            new BlobServiceClient(_opts.StorageConnectionString).GetBlobContainerClient(_opts.BlobContainerName);

        private TableClient Table(string name) =>
            new TableClient(_opts.StorageConnectionString, name);

        private QueueClient Queue =>
            new QueueClient(_opts.StorageConnectionString, _opts.QueueName);

        // ---------- Blob Storage: drone imagery ----------

        public async Task<string> UploadImageAsync(IFormFile file, string uploadedBy)
        {
            var container = Container;
            await container.CreateIfNotExistsAsync(PublicAccessType.None);

            var safeName = $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}";
            var blob = container.GetBlobClient(safeName);

            await using var stream = file.OpenReadStream();
            await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = file.ContentType });
            await blob.SetMetadataAsync(new Dictionary<string, string>
            {
                ["uploadedby"] = uploadedBy,
                ["uploadedat"] = DateTimeOffset.UtcNow.ToString("O"),
            });
            return safeName;
        }

        public async Task<List<BlobItemInfo>> ListImagesAsync(int max = 50)
        {
            var container = Container;
            await container.CreateIfNotExistsAsync();

            var items = new List<BlobItemInfo>();
            await foreach (var b in container.GetBlobsAsync())
            {
                items.Add(new BlobItemInfo
                {
                    Name = b.Name,
                    Uri = container.GetBlobClient(b.Name).Uri.ToString(),
                    SizeBytes = b.Properties.ContentLength ?? 0,
                    LastModified = b.Properties.LastModified,
                });
                if (items.Count >= max) break;
            }
            return items;
        }

        // ---------- Table Storage: audit entries written by the blob-triggered function ----------

        public async Task<List<ImageAuditEntry>> GetAuditEntriesAsync(int max = 50)
        {
            var table = Table(_opts.AuditTableName);
            await table.CreateIfNotExistsAsync();

            var entries = new List<ImageAuditEntry>();
            await foreach (var e in table.QueryAsync<TableEntity>(maxPerPage: max))
            {
                entries.Add(new ImageAuditEntry
                {
                    BlobName = e.GetString("BlobName") ?? e.RowKey,
                    SizeBytes = e.GetInt64("SizeBytes") ?? 0,
                    ContentType = e.GetString("ContentType") ?? "",
                    UploadedAt = e.GetDateTimeOffset("UploadedAt") ?? e.Timestamp ?? DateTimeOffset.MinValue,
                    Status = e.GetString("Status") ?? "",
                });
                if (entries.Count >= max) break;
            }
            return entries.OrderByDescending(e => e.UploadedAt).ToList();
        }

        // ---------- Table Storage: high-frequency telemetry ----------

        /// <summary>Inverted-ticks RowKey makes the newest reading per device sort first.</summary>
        private static string NewRowKey() =>
            (DateTimeOffset.MaxValue.Ticks - DateTimeOffset.UtcNow.Ticks).ToString("D19");

        public async Task AddTelemetryAsync(TelemetryReading reading)
        {
            var table = Table(_opts.TelemetryTableName);
            await table.CreateIfNotExistsAsync();

            var entity = new TableEntity(reading.DeviceId, NewRowKey())
            {
                ["DeviceId"] = reading.DeviceId,
                ["Timestamp"] = reading.Timestamp,
                ["SoilMoisture"] = reading.SoilMoisture,
                ["TemperatureC"] = reading.TemperatureC,
                ["Humidity"] = reading.Humidity,
                ["BatteryLevel"] = reading.BatteryLevel,
            };
            await table.AddEntityAsync(entity);
        }

        /// <summary>Reads the newest telemetry rows. Used by the HTTP GET function and as fallback.</summary>
        public async Task<List<TelemetryReading>> GetLatestTelemetryAsync(int count = 25, string? deviceId = null)
        {
            var table = Table(_opts.TelemetryTableName);
            await table.CreateIfNotExistsAsync();

            var readings = new List<TelemetryReading>();
            string? filter = string.IsNullOrWhiteSpace(deviceId)
                ? null
                : $"PartitionKey eq '{deviceId.Replace("'", "''")}'";

            await foreach (var e in table.QueryAsync<TableEntity>(filter: filter, maxPerPage: count))
            {
                readings.Add(Map(e));
                if (readings.Count >= count) break;
            }
            return readings;
        }

        internal static TelemetryReading Map(TableEntity e) => new()
        {
            DeviceId = e.GetString("DeviceId") ?? e.PartitionKey,
            Timestamp = e.GetDateTimeOffset("Timestamp") ?? e.Timestamp ?? DateTimeOffset.MinValue,
            SoilMoisture = e.GetDouble("SoilMoisture") ?? 0,
            TemperatureC = e.GetDouble("TemperatureC") ?? 0,
            Humidity = e.GetDouble("Humidity") ?? 0,
            BatteryLevel = e.GetDouble("BatteryLevel") ?? 0,
        };

        // ---------- Queue Storage: irrigation commands (fallback when POST function not set) ----------

        public async Task EnqueueIrrigationAsync(IrrigationCommand command)
        {
            var queue = Queue;
            await queue.CreateIfNotExistsAsync();
            var json = JsonSerializer.Serialize(command);
            await queue.SendMessageAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
        }
    }
}
