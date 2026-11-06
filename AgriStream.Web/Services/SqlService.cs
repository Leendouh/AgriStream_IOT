using AgriStream.Web.Models;
using AgriStream.Web.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AgriStream.Web.Services
{
    /// <summary>
    /// Data access for Azure SQL Database: the <c>Users</c> table (login credentials + roles)
    /// and the <c>DeviceRegistry</c> master table updated by the queue-triggered function.
    /// </summary>
    public class SqlService
    {
        private readonly AzureOptions _opts;
        private readonly ILogger<SqlService> _logger;

        public SqlService(IOptions<AzureOptions> opts, ILogger<SqlService> logger)
        {
            _opts = opts.Value;
            _logger = logger;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_opts.SqlConnectionString);

        private SqlConnection Open() => new(_opts.SqlConnectionString);

        /// <summary>
        /// Idempotently creates the relational schema and seeds demo users/devices.
        /// Safe to call at every startup.
        /// </summary>
        public async Task EnsureSchemaAsync()
        {
            if (!IsConfigured) return;

            const string ddl = """
                IF OBJECT_ID('dbo.Users','U') IS NULL
                CREATE TABLE dbo.Users(
                    Id INT IDENTITY(1,1) PRIMARY KEY,
                    Username NVARCHAR(100) NOT NULL UNIQUE,
                    PasswordHash NVARCHAR(128) NOT NULL,
                    Salt NVARCHAR(64) NOT NULL,
                    Role NVARCHAR(30) NOT NULL);

                IF OBJECT_ID('dbo.DeviceRegistry','U') IS NULL
                CREATE TABLE dbo.DeviceRegistry(
                    DeviceId NVARCHAR(50) PRIMARY KEY,
                    Name NVARCHAR(100) NOT NULL,
                    DeviceType NVARCHAR(50) NOT NULL,
                    Location NVARCHAR(100) NOT NULL,
                    Status NVARCHAR(30) NOT NULL DEFAULT 'Idle',
                    LastCommandAt DATETIME2 NULL,
                    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME());
                """;

            await EnsureDatabaseExistsAsync();

            await using var conn = Open();
            await conn.OpenAsync();
            await using (var cmd = new SqlCommand(ddl, conn))
                await cmd.ExecuteNonQueryAsync();

            await SeedUsersAsync(conn);
            await SeedDevicesAsync(conn);
        }

        /// <summary>
        /// Creates the database named in the connection string if it does not exist.
        /// On Azure SQL the database is provisioned in the portal, so this is a no-op;
        /// it matters for local dev (e.g. LocalDB) where nothing pre-creates it.
        /// </summary>
        private async Task EnsureDatabaseExistsAsync()
        {
            var csb = new SqlConnectionStringBuilder(_opts.SqlConnectionString);
            var dbName = csb.InitialCatalog;
            if (string.IsNullOrWhiteSpace(dbName)) return;

            csb.InitialCatalog = "master";
            try
            {
                await using var master = new SqlConnection(csb.ConnectionString);
                await master.OpenAsync();

                await using var cmd = new SqlCommand(
                    $"IF DB_ID(@db) IS NULL CREATE DATABASE [{dbName}]", master);
                cmd.Parameters.AddWithValue("@db", dbName);
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                // Not fatal: on Azure SQL the database already exists and logins
                // may not have permission to reach 'master'. Continue and let the
                // normal connection attempt surface any real problem.
                _logger.LogDebug(ex, "Could not verify/create database {Db}; continuing.", dbName);
            }
        }

        private async Task SeedUsersAsync(SqlConnection conn)
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.Users", conn);
            if ((int)(await check.ExecuteScalarAsync() ?? 0) > 0) return;

            var seeds = new[]
            {
                ("admin", "Admin@123", "Admin"),
                ("farmer", "Farm@123", "Farmer"),
                ("agronomist", "Agro@123", "Agronomist"),
            };
            foreach (var (u, p, r) in seeds)
            {
                var salt = PasswordHasher.NewSalt();
                await using var ins = new SqlCommand(
                    "INSERT INTO dbo.Users(Username,PasswordHash,Salt,Role) VALUES(@u,@h,@s,@r)", conn);
                ins.Parameters.AddWithValue("@u", u);
                ins.Parameters.AddWithValue("@h", PasswordHasher.Hash(p, salt));
                ins.Parameters.AddWithValue("@s", salt);
                ins.Parameters.AddWithValue("@r", r);
                await ins.ExecuteNonQueryAsync();
            }
            _logger.LogInformation("Seeded default users (admin, farmer, agronomist).");
        }

        private async Task SeedDevicesAsync(SqlConnection conn)
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.DeviceRegistry", conn);
            if ((int)(await check.ExecuteScalarAsync() ?? 0) > 0) return;

            var seeds = new[]
            {
                ("PUMP-01", "North Field Pump", "IrrigationPump", "North Field"),
                ("PUMP-02", "Orchard Pump", "IrrigationPump", "West Orchard"),
                ("PUMP-03", "Greenhouse Valve", "IrrigationValve", "Greenhouse 2"),
                ("SENSOR-A1", "North Field Sensor", "SoilSensor", "North Field"),
                ("SENSOR-B2", "Orchard Sensor", "SoilSensor", "West Orchard"),
            };
            foreach (var (id, name, type, loc) in seeds)
            {
                await using var ins = new SqlCommand(
                    "INSERT INTO dbo.DeviceRegistry(DeviceId,Name,DeviceType,Location) VALUES(@i,@n,@t,@l)", conn);
                ins.Parameters.AddWithValue("@i", id);
                ins.Parameters.AddWithValue("@n", name);
                ins.Parameters.AddWithValue("@t", type);
                ins.Parameters.AddWithValue("@l", loc);
                await ins.ExecuteNonQueryAsync();
            }
            _logger.LogInformation("Seeded device registry.");
        }

        public async Task<AppUser?> ValidateUserAsync(string username, string password)
        {
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id,Username,PasswordHash,Salt,Role FROM dbo.Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", username);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;

            var user = new AppUser
            {
                Id = r.GetInt32(0),
                Username = r.GetString(1),
                PasswordHash = r.GetString(2),
                Salt = r.GetString(3),
                Role = r.GetString(4),
            };
            return PasswordHasher.Verify(password, user.Salt, user.PasswordHash) ? user : null;
        }

        public async Task<List<AppUser>> GetUsersAsync()
        {
            var users = new List<AppUser>();
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT Id,Username,PasswordHash,Salt,Role FROM dbo.Users ORDER BY Username", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                users.Add(new AppUser { Id = r.GetInt32(0), Username = r.GetString(1), PasswordHash = r.GetString(2), Salt = r.GetString(3), Role = r.GetString(4) });
            return users;
        }

        public async Task<List<Device>> GetDevicesAsync()
        {
            var devices = new List<Device>();
            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(
                "SELECT DeviceId,Name,DeviceType,Location,Status,LastCommandAt,UpdatedAt FROM dbo.DeviceRegistry ORDER BY DeviceId", conn);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                devices.Add(new Device
                {
                    DeviceId = r.GetString(0),
                    Name = r.GetString(1),
                    DeviceType = r.GetString(2),
                    Location = r.GetString(3),
                    Status = r.GetString(4),
                    LastCommandAt = r.IsDBNull(5) ? null : r.GetDateTime(5),
                    UpdatedAt = r.GetDateTime(6),
                });
            return devices;
        }
    }
}
