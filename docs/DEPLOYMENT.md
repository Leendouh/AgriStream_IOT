# AgriStream IoT — Azure Deployment Guide

Step-by-step instructions to provision every Azure resource and deploy the
solution. Everything can be done in the **Azure Portal** + **Visual Studio**
publish; Azure CLI equivalents are shown at the end.

## Current progress

| Step | Resource | Status |
|---|---|---|
| 1. Resource group | `rg-agristream` (or your chosen name) | ✅ Done |
| 2. Azure SQL | server `agristream111`, db `agristream` (Basic, LRS backup) | ✅ Done — tables + seed data verified |
| 3. Storage account | `agristreamst10381088` (Standard, LRS) | ✅ Done — `drone-images`, `telemetryd`, `irrigation-queue` verified |
| 4. Function App | `func-agristream-10381088` | ⬜ To do |
| 5. App Service | `10381088` on plan `asp-agristream` (S1) | ⬜ To do |
| 7. Application Insights | `ai-agristream` | ⬜ To do |
| 8–9. Load test + autoscale | — | ⬜ To do |

**Verified so far** (local app run against real Azure): login via `Users`
table, telemetry rows landing in `telemetryd`, irrigation message enqueued on
`irrigation-queue`, image blob uploaded to `drone-images`.

Region in use: **South Africa North**.

---

## 1. Resource group — DONE

## 2. Azure SQL Database — DONE (task b)

- Server `agristream111` (SQL + Microsoft Entra auth), database `agristream`,
  Basic tier, LRS backups.
- **Networking**: ✅ "Allow Azure services and resources" enabled; client IP added.
- The web app created `dbo.Users` + `dbo.DeviceRegistry` and seeded them on
  first connect (verified: 3 users, 5 devices).
- **ADO.NET connection string** (portal → database → *Connection strings*):
  `Server=tcp:agristream111.database.windows.net,1433;Initial Catalog=agristream;...`
  — already in `appsettings.Development.json` for local runs; it also goes into
  the App Service settings in step 5.

## 3. Azure Storage account — DONE (task c)

- `agristreamst10381088` (Standard, **LRS** — the choice analysed in Section B).
- Container `drone-images` (private), table `telemetryd`, queue
  `irrigation-queue` — all verified working.
- Tables `ImageAudit` and `IrrigationEvents` don't exist yet — the functions
  create them on first run (`CreateIfNotExists`), no action needed.
- **Connection string**: portal → storage account → *Security + networking →
  Access keys* → `key1` — already in `appsettings.Development.json`; it also
  goes into App Service / Function App settings.

## 4. Function App (task d) — NEXT

1. Portal → **Function App** → **Create**:
   - Name `func-agristream-10381088`
   - Runtime stack: **.NET 8 (LTS), Isolated worker model**
   - Hosting: **Consumption**
   - Storage: select `agristreamst10381088` (this automatically sets
     `AzureWebJobsStorage` to your storage account — the same account the
     queue/tables/blobs live in).
2. Once created → **Settings → Environment variables** (Configuration) → add:

   | Name | Value |
   |---|---|
   | `SqlConnectionString` | the ADO.NET string from step 2 (with real password) |
   | `TelemetryTableName` | `telemetryd` |
   | `AuditTableName` | `ImageAudit` |
   | `EventsTableName` | `IrrigationEvents` |
   | `QueueName` | `irrigation-queue` |
   | `APPLICATIONINSIGHTS_CONNECTION_STRING` | from step 7 |

3. **Publish the functions**: in Visual Studio, right-click
   `AgriStream.Functions` → **Publish** → **Azure** → **Azure Function App
   (Windows)** → select `func-agristream-10381088` → **Finish → Publish**.
   (Alternative: `func azure functionapp publish func-agristream-10381088`.)
4. In the portal, open the Function App → **Functions** — you should see all four:
   `DroneImageAuditFunction`, `GetTelemetryFunction`, `QueueIrrigationFunction`,
   `ProcessIrrigationFunction`.
5. Get the function URLs + keys for the web app:
   - Open `GetTelemetryFunction` → **Get function URL** → copy
     (`https://func-agristream-10381088.azurewebsites.net/api/telemetry?code=…`).
   - Do the same for `QueueIrrigationFunction` → `…/api/irrigation?code=…`.

## 5. App Service for the MVC app (task a)

1. Portal → **App Services** → **Create → Web App**:
   - Name `10381088` (→ URL `https://10381088.azurewebsites.net`)
   - Runtime: **.NET 8 (LTS)**, Windows
   - **Plan**: create `asp-agristream` with SKU **Standard S1** or higher —
     *autoscale is not available on Free/Shared/Basic tiers.*
2. Publish `AgriStream.Web` (right-click → Publish → Azure App Service).
   Confirm the site loads at the URL.
3. Web app → **Settings → Environment variables** → add
   (double underscore `__` maps to the `:` in config keys):

   | Name | Value |
   |---|---|
   | `Azure__SqlConnectionString` | ADO.NET string from step 2 |
   | `Azure__StorageConnectionString` | storage connection string from step 3 |
   | `Azure__TelemetryFunctionUrl` | full GET function URL incl. `?code=` |
   | `Azure__IrrigationFunctionUrl` | full POST function URL incl. `?code=` |
   | `APPLICATIONINSIGHTS_CONNECTION_STRING` | from step 7 |

   (`BlobContainerName`, table and queue names already default correctly in
   `appsettings.json` — `drone-images`, `telemetryd`, `irrigation-queue` — only
   set them if you chose different names.)

## 6. Verify the end-to-end pipeline

1. Browse to `https://10381088.azurewebsites.net` → **Login** as
   `farmer / Farm@123`.
2. **Telemetry** → push a few readings (or run `scripts/Send-Telemetry.ps1`).
3. **Dashboard** → telemetry table populated via the GET function; device list
   from SQL.
4. Click **Irrigate** on `PUMP-01` → after a few seconds refresh → status shows
   `Irrigating` (proof the queue trigger ran and updated SQL). NOTE: the queue
   already holds one message from local testing — it will be consumed the
   moment the function app starts.
5. **Drone Imagery** → upload a JPG/PNG → appears under Blob Storage → a few
   seconds later an audit row appears under Processing audit (proof the blob
   trigger ran and wrote to `ImageAudit`).

## 7. Monitoring (task f)

1. Portal → **Application Insights** → **Create** (`ai-agristream`), workspace-based.
2. Copy its **connection string** into both the web app and function app
   settings (steps 4 and 5 above). The code already calls
   `AddApplicationInsightsTelemetry()` — requests, dependencies, exceptions and
   traces flow automatically.
3. During the load test, open:
   - App Service → **Monitoring → Metrics** → `CPU Time`, `Requests`,
     `Response Time`, `Data In/Out`.
   - Application Insights → **Live Metrics** for real-time view.
   - Function App → **Functions → Monitor** / **Invocations** for execution counts.
   - Azure SQL → **Metrics** → `DTU percentage`.

## 8. Load testing (task e)

```powershell
.\scripts\LoadTest.ps1 -BaseUrl "https://10381088.azurewebsites.net" -TotalRequests 3000 -Concurrency 50
```

Run it for several minutes (raise `-TotalRequests`). Optional heavier traffic:
**Azure Load Testing** resource in the portal (create → Quick test → paste the
URL, set virtual users/duration) or a JMeter HTTP sampler pointed at the site.
Also run `scripts\Send-Telemetry.ps1` concurrently to generate write load.

## 9. Auto-scaling (task g)

Autoscale requires plan **S1+** (see step 5).

1. App Service → **Scale out (App Service plan)** → **Scale out** →
   **Custom autoscale**.
2. Add a rule: *Metric* `CPU Percentage` (or Memory Percentage) → *Operator*
   `Greater than` → *Threshold* `70` → *Duration* `5 minutes` →
   *Operation* `Increase count by 1`.
3. Add a scale-**in** rule: `CPU Percentage` `Less than` `30` → `Decrease count
   by 1`. Set instance limits e.g. min 1, default 1, **max 4**.
4. Save, then run the load test. Evidence:
   - App Service → **Scale out** → **Run history** shows the scale-out event.
   - Metrics chart shows instance count increasing and CPU per instance
     dropping as new instances come online.
   - Diagnose and solve problems → **Availability and Performance** also lists
     scale events.

## 10. Optional: Azure CLI quick-reference

```bash
az group create -n rg-agristream -l southafricanorth
az sql server create -n agristream111 -g rg-agristream -l southafricanorth -u <admin> -p '<pw>'
az sql db create -n agristream -g rg-agristream -s agristream111 --edition Basic --backup-storage-redundancy Local
az storage account create -n agristreamst10381088 -g rg-agristream --sku Standard_LRS
az storage container create -n drone-images --account-name agristreamst10381088
az storage table create -n telemetryd --account-name agristreamst10381088
az storage queue create -n irrigation-queue --account-name agristreamst10381088
az functionapp create -n func-agristream-10381088 -g rg-agristream --consumption-plan-location southafricanorth \
  --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --storage-account agristreamst10381088
az appservice plan create -n asp-agristream -g rg-agristream --sku S1
az webapp create -n 10381088 -g rg-agristream -p asp-agristream --runtime "dotnet:8"
# (if the identifier fails, run `az webapp list-runtimes --os-type windows` for the exact name)
```
