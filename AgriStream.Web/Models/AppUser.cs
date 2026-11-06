namespace AgriStream.Web.Models
{
    /// <summary>A user account stored in the Azure SQL <c>Users</c> table.</summary>
    public class AppUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty; // Farmer | Agronomist | Admin
    }
}
