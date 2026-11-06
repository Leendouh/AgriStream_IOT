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
        public static string TelemetryTable => Get("TelemetryTableName", "Telemetry");
        public static string AuditTable => Get("AuditTableName", "ImageAudit");
        public static string EventsTable => Get("EventsTableName", "IrrigationEvents");
        public static string QueueName => Get("QueueName", "irrigation-commands");

        public static string Get(string name, string fallback = "") =>
            Environment.GetEnvironmentVariable(name) ?? fallback;

        public static TableClient Table(string name) => new(StorageConn, name);
        public static QueueClient Queue() => new(StorageConn, QueueName);
        public static SqlConnection Sql() => new(SqlConn);
    }
}
