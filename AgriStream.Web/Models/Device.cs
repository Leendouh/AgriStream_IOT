namespace AgriStream.Web.Models
{
    /// <summary>A row in the Azure SQL <c>DeviceRegistry</c> table (master device registry).</summary>
    public class Device
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DeviceType { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Status { get; set; } = "Idle";
        public DateTime? LastCommandAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
