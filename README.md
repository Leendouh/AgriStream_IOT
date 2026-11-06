# AgriStream IoT

Smart-farm monitoring platform for the Azure practicum: an ASP.NET Core MVC web
app plus four Azure Functions that process drone imagery, telemetry and
irrigation commands.

## Solution layout

- **`AgriStream.Web/`** — MVC app (App Service). Azure SQL auth, Blob uploads,
  Table telemetry, irrigation triggers, Application Insights.
- **`AgriStream.Functions/`** — .NET 8 isolated Azure Functions:
  `DroneImageAuditFunction` (blob trigger), `GetTelemetryFunction` (HTTP GET),
  `QueueIrrigationFunction` (HTTP POST → queue), `ProcessIrrigationFunction`
  (queue trigger → Azure SQL).
- **`sql/schema.sql`** — `Users` + `DeviceRegistry` schema and seed.
- **`scripts/`** — `LoadTest.ps1` (task e), `Send-Telemetry.ps1` (sensor sim).
- **`docs/DEPLOYMENT.md`** — full Azure provisioning + publish + autoscale steps.
- **`docs/REPORT.md`** — submission report: service descriptions, screenshot
  checklist, Section B theory answers.

## Build

```powershell
dotnet build AgriStream.sln
```

## Run locally

```powershell
dotnet run --project AgriStream.Web
```

The app runs without Azure resources — pages that need cloud resources show a
"not configured" notice. For local storage use Azurite
(`UseDevelopmentStorage=true`, already set in `appsettings.Development.json`)
and Azure Functions Core Tools (`func start` in `AgriStream.Functions`).

## Deploy

See `docs/DEPLOYMENT.md`. Configure the `Azure:*` app settings on the App
Service (SQL + storage connection strings, function URLs) and the function app
settings (`SqlConnectionString`, table/queue names).

Seeded logins: `admin/Admin@123` (Admin), `farmer/Farm@123` (Farmer),
`agronomist/Agro@123` (Agronomist).
