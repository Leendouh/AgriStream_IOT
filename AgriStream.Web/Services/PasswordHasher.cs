using System.Security.Cryptography;
using System.Text;

namespace AgriStream.Web.Services
{
    /// <summary>Salted SHA-256 password hashing for credentials stored in Azure SQL.</summary>
    public static class PasswordHasher
    {
        public static string Hash(string password, string salt)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));
            return Convert.ToHexString(bytes);
        }

        public static string NewSalt() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

        public static bool Verify(string password, string salt, string expectedHash) =>
            string.Equals(Hash(password, salt), expectedHash, StringComparison.OrdinalIgnoreCase);
    }
}
