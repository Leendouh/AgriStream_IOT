namespace AgriStream.Web.Models
{
    /// <summary>An audit row written to the <c>ImageAudit</c> table by the blob-triggered function.</summary>
    public class ImageAuditEntry
    {
        public string BlobName { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTimeOffset UploadedAt { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}
