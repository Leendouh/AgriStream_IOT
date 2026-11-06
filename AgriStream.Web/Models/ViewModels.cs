using System.ComponentModel.DataAnnotations;

namespace AgriStream.Web.Models
{
    public class LoginViewModel
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }

    public class DashboardViewModel
    {
        public List<TelemetryReading> Telemetry { get; set; } = new();
        public List<Device> Devices { get; set; } = new();
        public string TelemetrySource { get; set; } = string.Empty;
        public string? Message { get; set; }
        public bool MessageIsError { get; set; }
    }

    public class MediaViewModel
    {
        public List<BlobItemInfo> Images { get; set; } = new();
        public List<ImageAuditEntry> AuditEntries { get; set; } = new();
    }

    public class BlobItemInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTimeOffset? LastModified { get; set; }
    }

    public class TelemetryViewModel
    {
        public List<TelemetryReading> Readings { get; set; } = new();

        [Required, Display(Name = "Device Id")]
        public string DeviceId { get; set; } = "SENSOR-A1";

        [Range(-20, 80), Display(Name = "Soil Moisture (%)")]
        public double SoilMoisture { get; set; } = 35;

        [Range(-40, 80), Display(Name = "Temperature (°C)")]
        public double TemperatureC { get; set; } = 24;

        [Range(0, 100), Display(Name = "Humidity (%)")]
        public double Humidity { get; set; } = 55;

        [Range(0, 100), Display(Name = "Battery (%)")]
        public double BatteryLevel { get; set; } = 90;
    }

    public class AdminViewModel
    {
        public List<AppUser> Users { get; set; } = new();
        public List<Device> Devices { get; set; } = new();
    }
}
