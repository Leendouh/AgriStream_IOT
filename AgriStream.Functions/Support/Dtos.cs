namespace AgriStream.Functions.Support
{
    /// <summary>Telemetry row returned by the HTTP GET function.</summary>
    public class TelemetryReading
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTimeOffset Timestamp { get; set; }
        public double SoilMoisture { get; set; }
        public double TemperatureC { get; set; }
        public double Humidity { get; set; }
        public double BatteryLevel { get; set; }
    }

    /// <summary>Irrigation command accepted by the HTTP POST function and placed on the queue.</summary>
    public class IrrigationCommand
    {
        public string DeviceId { get; set; } = string.Empty;
        public int DurationSeconds { get; set; } = 60;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
