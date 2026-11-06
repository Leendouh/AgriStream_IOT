using AgriStream.Functions.Support;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AgriStream.Functions.Functions
{
    /// <summary>
    /// BLOB TRIGGER - fires for every file uploaded to the "drone-images" container.
    /// Logs the upload details and writes a processing-status audit row into the
    /// ImageAudit Azure Table.
    /// </summary>
    public class DroneImageAuditFunction
    {
        private readonly ILogger<DroneImageAuditFunction> _logger;

        public DroneImageAuditFunction(ILogger<DroneImageAuditFunction> logger) => _logger = logger;

        [Function(nameof(DroneImageAuditFunction))]
        public async Task Run(
            [BlobTrigger("drone-images/{name}", Connection = "AzureWebJobsStorage")] Stream stream,
            string name)
        {
            var size = stream.Length;

            string contentType = "application/octet-stream";
            try
            {
                var props = await new BlobClient(Env.StorageConn, "drone-images", name).GetPropertiesAsync();
                contentType = props.Value.ContentType;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read blob properties for {Name}.", name);
            }

            _logger.LogInformation("Drone image uploaded: {Name} ({Size} bytes, {ContentType})",
                name, size, contentType);

            var table = Env.Table(Env.AuditTable);
            await table.CreateIfNotExistsAsync();

            var entity = new TableEntity("uploads", $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}")
            {
                ["BlobName"] = name,
                ["SizeBytes"] = size,
                ["ContentType"] = contentType,
                ["UploadedAt"] = DateTimeOffset.UtcNow,
                ["Status"] = "Received - queued for processing",
            };
            await table.AddEntityAsync(entity);

            _logger.LogInformation("Audit entry written to {Table} for blob {Name}.", Env.AuditTable, name);
        }
    }
}
