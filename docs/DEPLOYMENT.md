# AgriStream IoT — Azure Deployment Guide

Step-by-step instructions to provision every Azure resource and deploy the
solution. Everything can be done in the **Azure Portal** + **Visual Studio**
publish; Azure CLI equivalents are shown where helpful.

> Replace `st123456` with your student number everywhere so your URL matches the
> required format (`http://st123456.azurewebsites.net`).

---

## 0. Prerequisites

- An Azure subscription (student subscription is fine).
- Visual Studio 2022 with the **ASP.NET and web development** and
  **Azure development** workloads.
- This repository cloned locally.

Suggested resource names (keep them consistent):

| Resource | Suggested name |
|---|---|
| Resource group | `rg-agristream` |
| SQL server | `sql-agristream-st123456` |
| SQL database | `agristreamdb` |
| Storage account | `stagristreamst123456` (lowercase, no dashes) |
| Function App | `func-agristream-st123456` |
| App Service plan | `asp-agristream` |
| App Service (web app) | `st123456` → `st123456.azurewebsites.net` |
| Application Insights | `ai-agristream` |

Region: pick one region (e.g. `South Africa North` or `West Europe`) and use it
for everything.

---

## 1. Resource group

Portal → **Resource groups** → **Create** → name `rg-agristream`, choose region.

## 2. Azure SQL Database (task b)

1. Portal → **SQL databases** → **Create**:
   - Server: **Create new** → `sql-agristream-st123456`, admin login
     `agriadmin` + a strong password (save it).
   - Compute: **Basic** (5 DTU) is sufficient and cheapest.
2. After creation, on the **SQL server** → **Networking**:
   - Tick **Allow Azure services and resources to access this server** (required
     so the App Service and Function App can reach it).
   - Add your **client IP** so you can connect from SSMS/Visual Studio.
3. Open the database → **Query editor** → sign in → run `sql/schema.sql`
   (creates `Users` + `DeviceRegistry`). The web app also auto-creates them on
   first start, so this step is optional but good evidence.
4. Copy the **ADO.NET connection string** (database → *Connection strings* →
   ADO.NET) and put your password in `{your_password}`.

## 3. Azure Storage account (task c)

1. Portal → **Storage accounts** → **Create**:
   - Name `stagristreamst123456`, redundancy **LRS** (discussed in Section B).
2. Under **Data storage** create:
   - **Blob container**: `drone-images` (private access).
   - **Tables**: `Telemetry`, `ImageAudit`, `IrrigationEvents`.
   - **Queue**: `irrigation-commands`.
   (The code calls `CreateIfNotExists`, so these also self-create — creating
   them now gives you instant portal screenshots.)
3. **Security + networking → Access keys** → copy the
   **Connection string** for `key1`.

## 4. Function App (task d)

1. Portal → **Function App** → **Create**:
   - Name `func-agristream-st123456`
   - Runtime stack: **.NET 8 (LTS), Isolated worker model**
   - Hosting: **Consumption**
   - Storage: select `stagristreamst123456` (this automatically sets
     `AzureWebJobsStorage` to your storage account - the same account the
     queues/tables/blobs live in).
2. Once created → **Settings → Environment variables** (Configuration) → add:

   | Name | Value |
   |---|---|
   | `SqlConnectionString` | ADO.NET string from step 2 |
   | `TelemetryTableName` | `Telemetry` |
   | `AuditTableName` | `ImageAudit` |
   | `EventsTableName` | `IrrigationEvents` |
   | `QueueName` | `irrigation-commands` |
   | `APPLICATIONINSIGHTS_CONNECTION_STRING` | connection string from step 7 |

3. **Publish the functions**: in Visual Studio, right-click
   `AgriStream.Functions` → **Publish** → **Azure** → **Azure Function App
   (Windows/Linux)** → select `func-agristream-st123456` → **Finish → Publish**.
   (Alternative: `func azure functionapp publish func-agristream-st123456` with
   Azure Functions Core Tools.)
4. In the portal, open the Function App → **Functions** — you should see all four:
   `DroneImageAuditFunction`, `GetTelemetryFunction`, `QueueIrrigationFunction`,
   `ProcessIrrigationFunction`.
5. Get the function URLs + keys for the web app:
   - Open `GetTelemetryFunction` → **Get function URL** → copy
     (`https://func-agristream-st123456.azurewebsites.net/api/telemetry?code=…`).
   - Do the same for `QueueIrrigationFunction` → `…/api/irrigation?code=…`.

## 5. App Service for the MVC app (task a)

1. Portal → **App Services** → **Create → Web App**:
   - Name `st123456` (→ URL `https://st123456.azurewebsites.net`)
   - Runtime: **.NET 8 (LTS)**, Windows
   - **Plan**: create `asp-agristream` with SKU **Standard S1** or higher —
     *autoscale is not available on Free/Shared/Basic tiers.*
2. Publish `AgriStream.Web` the same way (right-click → Publish → Azure App
   Service). Confirm the site loads at the URL.
3. Web app → **Settings → Environment variables** → add
   (note the double underscore `__` which maps to the `:` in config keys):

   | Name | Value |
   |---|---|
   | `Azure__SqlConnectionString` | ADO.NET string from step 2 |
   | `Azure__StorageConnectionString` | storage connection string from step 3 |
   | `Azure__TelemetryFunctionUrl` | full GET function URL incl. `?code=` |
   | `Azure__IrrigationFunctionUrl` | full POST function URL incl. `?code=` |
   | `APPLICATIONINSIGHTS_CONNECTION_STRING` | from step 7 |

   (`BlobContainerName`, table and queue names already default correctly in
   `appsettings.json` — only set them if you chose different names.)

## 6. Verify the end-to-end pipeline

1. Browse to `https://st123456.azurewebsites.net` → **Login** as
   `farmer / Farm@123`.
2. **Telemetry** → push a few readings (or run `scripts/Send-Telemetry.ps1`).
3. **Dashboard** → telemetry table populated via the GET function; device list
   from SQL.
4. Click **Irrigate** on `PUMP-01` → after a few seconds refresh → status shows
   `Irrigating` (proof the queue trigger ran and updated SQL).
5. **Drone Imagery** → upload a JPG/PNG → appears under Blob Storage → a few
   seconds later an audit row appears under Processing audit (proof the blob
   trigger ran).

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
.\scripts\LoadTest.ps1 -BaseUrl "https://st123456.azurewebsites.net" -TotalRequests 3000 -Concurrency 50
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
az group create -n rg-agristream -l westeurope
az sql server create -n sql-agristream-st123456 -g rg-agristream -l westeurope -u agriadmin -p '<pw>'
az sql db create -n agristreamdb -g rg-agristream -s sql-agristream-st123456 --edition Basic
az storage account create -n stagristreamst123456 -g rg-agristream --sku Standard_LRS
az storage container create -n drone-images --account-name stagristreamst123456
az storage table create -n Telemetry --account-name stagristreamst123456
az storage queue create -n irrigation-commands --account-name stagristreamst123456
az functionapp create -n func-agristream-st123456 -g rg-agristream --consumption-plan-location westeurope \
  --runtime dotnet-isolated --runtime-version 8 --functions-version 4 --storage-account stagristreamst123456
az appservice plan create -n asp-agristream -g rg-agristream --sku S1
az webapp create -n st123456 -g rg-agristream -p asp-agristream --runtime "DOTNET:8"
```
