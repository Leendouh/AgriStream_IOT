namespace AgriStream.Web.Models
{
    /// <summary>
    /// A single sensor reading stored as an entity in the Azure Table <c>Telemetry</c>.
    /// PartitionKey = device id; RowKey = inverted UTC ticks so newest rows sort first.
    /// </summary>
    public class TelemetryReading
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public double SoilMoisture { get; set; }
        public double TemperatureC { get; set; }
        public double Humidity { get; set; }
        public double BatteryLevel { get; set; }
    }
}
