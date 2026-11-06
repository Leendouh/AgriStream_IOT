namespace AgriStream.Web.Options
{
    /// <summary>
    /// Central configuration for all Azure resources used by AgriStream.
    /// Bound from the "Azure" section of appsettings.json / App Service configuration.
    /// </summary>
    public class AzureOptions
    {
        public const string SectionName = "Azure";

        /// <summary>Azure SQL Database ADO.NET connection string (user accounts + device registry).</summary>
        public string SqlConnectionString { get; set; } = string.Empty;

        /// <summary>Azure Storage account connection string (Blob, Table and Queue services).</summary>
        public string StorageConnectionString { get; set; } = string.Empty;

        /// <summary>Blob container that receives drone imagery uploads.</summary>
        public string BlobContainerName { get; set; } = "drone-images";

        /// <summary>Table that stores high-frequency sensor telemetry.</summary>
        public string TelemetryTableName { get; set; } = "telemetryd";

        /// <summary>Table the blob-triggered function writes image audit rows to.</summary>
        public string AuditTableName { get; set; } = "ImageAudit";

        /// <summary>Table the queue-triggered function writes irrigation events to.</summary>
        public string EventsTableName { get; set; } = "IrrigationEvents";

        /// <summary>Storage queue carrying irrigation commands to the queue-triggered function.</summary>
        public string QueueName { get; set; } = "irrigation-queue";

        /// <summary>Fully-qualified URL (incl. function key) of the HTTP GET telemetry function.</summary>
        public string TelemetryFunctionUrl { get; set; } = string.Empty;

        /// <summary>Fully-qualified URL (incl. function key) of the HTTP POST irrigation function.</summary>
        public string IrrigationFunctionUrl { get; set; } = string.Empty;
    }
}
