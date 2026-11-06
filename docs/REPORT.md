# AgriStream IoT — Practicum Submission Document

> Paste this content into your MS-Word submission document, replace the
> placeholders, and insert the screenshots listed in §4 at the indicated points.

**Student number:** `<your student number>`
**Application URL:** `https://<st123456>.azurewebsites.net`
**Source code:** `<link to your repo / zip folder>`

---

## 1. Architecture overview

```
                        ┌────────────────────────────────────────────┐
                        │            Azure App Service (S1)           │
 Browser / sensors ───► │      AgriStream.Web (ASP.NET Core MVC)      │
                        └───┬─────────┬──────────┬──────────┬────────┘
                            │ SQL     │ Blob     │ Table    │ HTTPS
                            ▼         ▼          ▼          ▼
                    ┌───────────┐ ┌────────┐ ┌────────┐ ┌──────────────────┐
                    │ Azure SQL │ │ Blob   │ │ Table  │ │ Azure Function   │
                    │  Users +  │ │ drone- │ │Telemetry│ │ App (Consumption)│
                    │  Device-  │ │ images │ │ImageAudit│ │  4 functions    │
                    │  Registry │ │        │ │IrrigEvents│ │                 │
                    └─────▲─────┘ └───┬────┘ └───▲────┘ └──┬───┬───┬───┬────┘
                          │           │          │         │   │   │   │
                          │           │ BlobTrigger        │   │   │   │
                          │           └────────────────────┘   │   │   │
                          │                        Table write─┘   │   │
                          │           HTTP GET (telemetry) ◄───────┘   │
                          │           HTTP POST (irrigation) ──┐       │
                          │                                    ▼       │
                          │                              Queue: irrig- │
                          │                              commands      │
                          │                                    │       │
                          └───── UPDATE DeviceRegistry ◄─ QueueTrigger ┘
```

The system ingests high-frequency soil-sensor telemetry into Azure Table
Storage, stores drone imagery in Azure Blob Storage, authenticates users and
keeps the master device registry in Azure SQL Database, and processes uploads
and irrigation commands through four Azure Functions. The App Service runs on
an S1 plan with a CPU-based autoscale rule, and Application Insights monitors
the whole pipeline.

---

## 2. Services implemented (Section A)

### (a) Web hosting — Azure App Service
`AgriStream.Web`, an ASP.NET Core 8 MVC application, is deployed to an Azure
App Service on a Standard S1 App Service plan at
`https://<st123456>.azurewebsites.net`. It serves the public landing page,
cookie-authenticated login, the farmer dashboard, telemetry, drone imagery and
admin pages.

### (b) Relational database — Azure SQL Database
Database `agristreamdb` on server `sql-agristream-<st123456>` holds:

- `Users(Id, Username, PasswordHash, Salt, Role)` — login credentials with
  salted SHA-256 passwords and roles `Farmer`, `Agronomist`, `Admin`.
- `DeviceRegistry(DeviceId, Name, DeviceType, Location, Status, LastCommandAt,
  UpdatedAt)` — the master device registry the queue-triggered function updates.

Schema script: `sql/schema.sql` (also auto-created + seeded by the app).

### (c) Cloud storage — Azure Storage
- **Blob Storage** — container `drone-images`; uploads from the *Drone Imagery*
  page land here (private access).
- **Table Storage** — `Telemetry` (sensor readings, `PartitionKey` = device id,
  inverted-tick `RowKey` so newest reads first), `ImageAudit` (written by the
  blob function), `IrrigationEvents` (written by the queue function).
- **Queue Storage** — `irrigation-commands` decouples the web app from device
  command processing.

### (d) Serverless data pipeline — four Azure Functions (.NET 8 isolated)

| Function | Trigger | What it does |
|---|---|---|
| `DroneImageAuditFunction` | **Blob** (`drone-images/{name}`) | Logs the upload and writes a status audit row to the `ImageAudit` table. |
| `GetTelemetryFunction` | **HTTP GET** (`/api/telemetry`) | Reads latest telemetry from the `Telemetry` table, returns JSON for the dashboard. |
| `QueueIrrigationFunction` | **HTTP POST** (`/api/irrigation`) | Validates an irrigation command and enqueues it on `irrigation-commands`. |
| `ProcessIrrigationFunction` | **Queue** (`irrigation-commands`) | Updates the device status to `Irrigating` in SQL `DeviceRegistry` and logs an `IrrigationEvents` row. |

### (e) Load testing
`scripts/LoadTest.ps1` fired `<N>` requests at the deployed site with `<C>`
concurrent workers over `<T>` minutes. Results: `<paste script output — req/s,
avg/p95 latency, failures>`. (Optionally: Azure Load Testing quick test with
`<X>` virtual users for `<Y>` minutes.)

### (f) Monitoring
Application Insights (`ai-agristream`) is connected to both the App Service and
the Function App. During the load test, App Service metrics showed `<describe:
CPU %, request count, response time>`; Live Metrics showed the requests
arriving in real time; Function App invocation counts showed the pipeline
processing without queue backlog growth beyond `<n>` messages.

### (g) Auto-scaling
The App Service plan has a custom autoscale rule: scale **out** +1 instance when
CPU > 70 % for 5 min, scale **in** −1 when CPU < 30 %, min 1 / max 4 instances.
During the load test, run history showed scale-out from 1 → `<n>` instances at
`<time>` and scale-in back to 1 after the test ended.

---

## 3. How the pipeline satisfies each requirement

| Requirement | Implementation evidence |
|---|---|
| Surges of telemetry without data loss | Table Storage writes are O(1)-per-entity and scale automatically; ingest also available via anonymous `POST /api/telemetry` for sensor fleets. |
| Bursts of drone imagery | Uploads go straight to Blob Storage; processing is asynchronous via the blob trigger so the web app never blocks on image processing. |
| Commands never lost | Irrigation uses a queue — the web tier gets a 202 immediately; the queue trigger retries/dead-letters on failure; durable messages survive app downtime. |
| Near-instant dashboard | Inverted-tick RowKey ordering means the GET function reads the newest rows with no sort or scan. |
| Scalable web tier | Stateless app (cookie auth only), S1 plan + CPU autoscale rule. |

---

## 4. Screenshot checklist (insert into the Word document)

1. Resource group `rg-agristream` showing all resources.
2. App Service overview blade (URL, status = Running, plan S1).
3. Deployed web app: landing page, login page, dashboard with data.
4. SQL database Overview + Query Editor showing `SELECT * FROM Users` and
   `DeviceRegistry` rows.
5. Storage account: `drone-images` container with blobs; `Telemetry`,
   `ImageAudit`, `IrrigationEvents` tables (Storage Browser); queue with
   message count.
6. Function App → Functions list showing all four functions; one invocation
   log each (Monitor blade).
7. Drone Imagery page showing a blob + its audit row (proves blob trigger).
8. Dashboard: click Irrigate → device status = `Irrigating` (proves queue
   trigger updated SQL).
9. Load test: `LoadTest.ps1` console output / Azure Load Testing results blade.
10. Monitoring: App Service metrics graph during load; App Insights Live
    Metrics; function invocations graph.
11. Autoscale: rule blade + Run history showing scale-out and scale-in events.

---

## 5. Section B — Theory answers

### B1. Disaster Recovery & Redundancy: LRS vs GRS

Azure Storage maintains multiple copies of data; the difference is **where** the
copies live.

**Locally Redundant Storage (LRS)** synchronously writes **three replicas within
a single datacenter** (or zone) in the primary region. It is the cheapest option,
delivers 11-nines (99.999999999 %) durability over a year, and protects against
disk/node/rack failures — but if the whole datacenter or region is lost, the data
is lost too, unless paired-zone recovery is offered by Microsoft.

**Geo-Redundant Storage (GRS)** also keeps three synchronous LRS replicas in the
primary region, but then **asynchronously** copies the data to a Microsoft-
paired region hundreds of kilometres away, where it is stored with LRS again —
six copies total and 16-nines durability. The secondary region is not readable
until Microsoft declares a regional failover (or a customer-initiated failover);
**RA-GRS** adds read access to the secondary for an extra cost. Async
replication means a small recovery-point gap (typically < 15 min) — the most
recent writes can be lost in a region-loss event.

**Applied to AgriStream:**

- **Critical historical crop-harvest records → GRS.** These records are
  irreplaceable business data used for audits, insurance and multi-season yield
  analysis. A region-level disaster must not destroy years of history, so the
  geo-copy in the paired region is worth the roughly 2× storage cost. If auditors
  need read access during an outage, RA-GRS is the better choice.
- **Temporary sensor logs → LRS.** High-frequency telemetry is cheap to
  regenerate — sensors keep transmitting, and transient readings lose value
  within hours. The small durability gain of GRS does not justify paying double
  on a data stream that grows to millions of rows. LRS already protects against
  the common failure modes (disk/rack loss) that actually matter here.

Decision rule: *replicability and business value drive the tier* — GRS for
data that cannot be re-created; LRS for high-volume transient data where cost
efficiency matters.

### B2. Stateless vs Stateful Architecture

Horizontal scale-out works by adding identical instances behind a load balancer;
any instance must be able to serve any request. That only works if the
application is **stateless** — no request may depend on data stored in the
memory, disk or session of a particular instance.

If session data or files were kept **locally**:

- **Broken requests.** Azure routes successive requests to different instances.
  A user whose session lives on instance A would appear logged out when their
  next request lands on instance B; a file written to A's disk is invisible to B.
- **Sticky sessions defeat the purpose.** Session affinity (ARR Affinity) can
  pin users to instances, but it prevents even load distribution, creates hot
  spots, and still loses the session if that instance is recycled or scaled in.
- **Unpredictable behaviour under autoscale.** Scale-in evicts an instance —
  all its local sessions/files die with it. That is exactly what the autoscale
  demo in task (g) must *not* show.

AgriStream is stateless by design: authentication uses an **encrypted cookie**
(no server-side session store), images go to **Blob Storage**, telemetry to
**Table Storage**, commands to the **queue**, and durable state to **Azure
SQL** — so any number of web instances can come and go safely. The same
principle applies even more strictly to **Azure Functions**: the Consumption
plan spins workers up and down per invocation count, each invocation may run on
a different host, and there is no machine affinity at all — all state must live
in the bound storage resources (tables, queues, blobs, SQL).

### B3. Serverless Triggers and Bindings

| Aspect | Trigger | Input binding | Output binding |
|---|---|---|---|
| **Definition** | The event/condition that causes a function execution and supplies the triggering payload. Exactly **one** trigger per function. | A declarative connection that **reads** data from another Azure resource into the function, without writing SDK code. | A declarative connection that **writes** data from the function to another resource. |
| **Purpose** | Start execution — schedule, HTTP call, new blob, new queue message, etc. | Fetch contextual/secondary data the function needs (e.g. a table row or blob) alongside the trigger payload. | Emit results — write rows, send queue messages, create blobs — just by returning values/setting properties. |
| **AgriStream example** | `DroneImageAuditFunction` fires on every new blob in `drone-images`; `GetTelemetryFunction`/`QueueIrrigationFunction` fire on HTTP requests; `ProcessIrrigationFunction` fires on `irrigation-commands` queue messages. | Equivalent role: the GET function **reads** the `Telemetry` table (implemented with the `Azure.Data.Tables` SDK inside the function; a `[TableInput]` input binding is the declarative alternative). | Equivalent role: the audit row written to `ImageAudit`, the message placed on `irrigation-commands`, and the `DeviceRegistry` update (implemented via SDK writes; `[TableOutput]`/`[QueueOutput]` attributes are the declarative alternative). |

**Key differences:** a trigger is mandatory and singular — it *causes* the run.
Bindings are optional helpers that *move data in or out* during the run. In the
**isolated worker model** used here, bindings are implemented as attributes
(`[BlobTrigger]`, `[HttpTrigger]`, `[QueueTrigger]`, `[QueueOutput]`…) on the
function signature; using the Azure SDK clients inside the body (as AgriStream
does for table reads/writes) achieves the same effect with more control over
queries and error handling.

---

## 6. Source code layout

```
AgriStream.sln
├── AgriStream.Web/            # ASP.NET Core 8 MVC application (App Service)
│   ├── Controllers/           # Account, Dashboard, Telemetry, Media, Irrigation, Admin
│   ├── Services/              # SqlService, StorageService, FunctionsClient, PasswordHasher
│   ├── Models/  Views/  Options/
│   └── Program.cs             # cookie auth, DI, App Insights, SQL schema auto-init
├── AgriStream.Functions/      # .NET 8 isolated Azure Functions
│   └── Functions/             # DroneImageAudit, GetTelemetry, QueueIrrigation, ProcessIrrigation
├── sql/schema.sql             # Azure SQL schema + seed
├── scripts/LoadTest.ps1       # concurrent load generator (task e)
├── scripts/Send-Telemetry.ps1 # simulated sensor fleet
└── docs/                      # DEPLOYMENT.md + this report
```
