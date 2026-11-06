namespace AgriStream.Web.Models
{
    /// <summary>Command message placed on the irrigation queue and processed by the queue-triggered function.</summary>
    public class IrrigationCommand
    {
        public string DeviceId { get; set; } = string.Empty;
        public int DurationSeconds { get; set; } = 60;
        public string RequestedBy { get; set; } = string.Empty;
        public DateTimeOffset IssuedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
