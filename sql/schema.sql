
-- ----------------------------------------------------------------------------
-- Users: login credentials + role membership (task b)
-- Passwords are stored as salted SHA-256 hashes (PasswordHash + Salt columns).
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.Users','U') IS NULL
CREATE TABLE dbo.Users(
    Id           INT IDENTITY(1,1) PRIMARY KEY,
    Username     NVARCHAR(100) NOT NULL UNIQUE,
    PasswordHash NVARCHAR(128) NOT NULL,   -- salted SHA-256 hash of the password
    Salt         NVARCHAR(64)  NOT NULL,
    Role         NVARCHAR(30)  NOT NULL    -- Farmer | Agronomist | Admin
);

-- ----------------------------------------------------------------------------
-- DeviceRegistry: master registry updated by the queue-triggered function (task d)
-- ----------------------------------------------------------------------------
IF OBJECT_ID('dbo.DeviceRegistry','U') IS NULL
CREATE TABLE dbo.DeviceRegistry(
    DeviceId      NVARCHAR(50) PRIMARY KEY,
    Name          NVARCHAR(100) NOT NULL,
    DeviceType    NVARCHAR(50)  NOT NULL,
    Location      NVARCHAR(100) NOT NULL,
    Status        NVARCHAR(30)  NOT NULL DEFAULT 'Idle',
    LastCommandAt DATETIME2 NULL,
    UpdatedAt     DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

-- ----------------------------------------------------------------------------
-- Seed devices (users are seeded by the app so hashes are generated in code)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.DeviceRegistry)
BEGIN
    INSERT INTO dbo.DeviceRegistry(DeviceId,Name,DeviceType,Location) VALUES
        ('PUMP-01',  'North Field Pump',   'IrrigationPump',  'North Field'),
        ('PUMP-02',  'Orchard Pump',       'IrrigationPump',  'West Orchard'),
        ('PUMP-03',  'Greenhouse Valve',   'IrrigationValve', 'Greenhouse 2'),
        ('SENSOR-A1','North Field Sensor', 'SoilSensor',      'North Field'),
        ('SENSOR-B2','Orchard Sensor',     'SoilSensor',      'West Orchard');
END

-- Quick verification queries
SELECT * FROM dbo.Users;
SELECT * FROM dbo.DeviceRegistry;
