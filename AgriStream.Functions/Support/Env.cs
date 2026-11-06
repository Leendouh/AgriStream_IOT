using Azure.Data.Tables;
using Azure.Storage.Queues;
using Microsoft.Data.SqlClient;

namespace AgriStream.Functions.Support
{
    /// <summary>Reads app settings and builds Azure SDK clients shared by all functions.</summary>
    public static class Env
    {
        public static string StorageConn => Get("AzureWebJobsStorage");
        public static string SqlConn => Get("SqlConnectionString");
        public static string TelemetryTable => Get("TelemetryTableName", "telemetryd");
        public static string AuditTable => Get("AuditTableName", "ImageAudit");
        public static string EventsTable => Get("EventsTableName", "IrrigationEvents");
        public static string QueueName => Get("QueueName", "irrigation-queue");

        public static string Get(string name, string fallback = "") =>
            Environment.GetEnvironmentVariable(name) ?? fallback;

        public static TableClient Table(string name) => new(StorageConn, name);

        // Base64 message encoding is required by Azurite and accepted by real Azure
        // Storage; ProcessIrrigationFunction decodes both forms defensively.
        public static QueueClient Queue() => new(StorageConn, QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        public static SqlConnection Sql() => new(SqlConn);
    }
}
