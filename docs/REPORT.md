# AgriStream IoT — Practicum Submission Document

> Paste this content into your MS-Word submission document, replace the
> remaining `<placeholders>` and `Figure [X]` numbers, and insert the
> screenshots listed in §4 at the indicated points.

**Student number:** `10381088`
**Application URL:** `https://st10381088agri-f8c0d2cxc4c2aagq.southafricanorth-01.azurewebsites.net`
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
                    ┌───────────┐ ┌────────┐ ┌─────────┐ ┌──────────────────┐
                    │ Azure SQL │ │ Blob   │ │ Table   │ │ Azure Function   │
                    │  Users +  │ │ drone- │ │telemetryd│ │ App (Consumption)│
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
                          │                          Queue: irrigation-│
                          │                                   queue    │
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
`https://st10381088agri-f8c0d2cxc4c2aagq.southafricanorth-01.azurewebsites.net`. It serves the public landing page,
cookie-authenticated login, the farmer dashboard, telemetry, drone imagery and
admin pages.

### (b) Relational database — Azure SQL Database
Database `agristream` on server `agristream111` (South Africa North, Basic tier)
holds:

- `Users(Id, Username, PasswordHash, Salt, Role)` — login credentials with
  salted SHA-256 passwords and roles `Farmer`, `Agronomist`, `Admin`.
- `DeviceRegistry(DeviceId, Name, DeviceType, Location, Status, LastCommandAt,
  UpdatedAt)` — the master device registry the queue-triggered function updates.

Schema script: `sql/schema.sql` (also auto-created and seeded idempotently by
`SqlService.EnsureSchemaAsync` on startup).

### (c) Cloud storage — Azure Storage
- **Blob Storage** — container `drone-images`; uploads from the *Drone Imagery*
  page land here (private access, GUID-prefixed names, uploader metadata).
- **Table Storage** — `telemetryd` (sensor readings, `PartitionKey` = device id,
  inverted-tick `RowKey` so newest reads first), `ImageAudit` (written by the
  blob function), `IrrigationEvents` (written by the queue function).
- **Queue Storage** — `irrigation-queue` decouples the web app from device
  command processing; messages are Base64-encoded, which both Azurite and the
  real service accept.

Storage account: `agristreamst10381088` (Standard performance, LRS redundancy —
see §5.1 for the rationale).

### (d) Serverless data pipeline — four Azure Functions (.NET 8 isolated)

| Function | Trigger | What it does |
|---|---|---|
| `DroneImageAuditFunction` | **Blob** (`drone-images/{name}`) | Logs the upload (name, size, content type) and writes a status audit row to the `ImageAudit` table. |
| `GetTelemetryFunction` | **HTTP GET** (`/api/telemetry?count=&deviceId=`) | Reads the newest telemetry rows from the `telemetryd` table and returns JSON for the dashboard. |
| `QueueIrrigationFunction` | **HTTP POST** (`/api/irrigation`) | Validates an irrigation command and enqueues it on `irrigation-queue`. |
| `ProcessIrrigationFunction` | **Queue** (`irrigation-queue`) | Updates the device status to `Irrigating` in SQL `DeviceRegistry` and appends an `IrrigationEvents` row. |

The MVC app calls the two HTTP functions through `FunctionsClient`. If a
function URL is not configured (e.g. before the Function App is deployed), it
falls back to reading the table directly / enqueuing the message itself, so the
pipeline degrades gracefully instead of failing.

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
| Surges of telemetry without data loss | Table Storage writes are O(1)-per-entity and scale automatically; ingest also available via anonymous `POST /api/telemetry` for sensor fleets (`scripts/Send-Telemetry.ps1`). |
| Bursts of drone imagery | Uploads go straight to Blob Storage; processing is asynchronous via the blob trigger, so the web app never blocks on image processing. |
| Commands never lost | Irrigation uses a queue — the web tier gets a 202 immediately; the queue trigger retries up to the max dequeue count and then moves failures to a poison queue; durable messages survive app downtime. |
| Near-instant dashboard | Inverted-tick RowKey ordering means the GET function reads the newest rows with no sort or scan. |
| Scalable web tier | Stateless app (cookie auth only, no server-side session), S1 plan + CPU autoscale rule. |
| Reliable command processing | `ProcessIrrigationFunction` discards malformed messages after logging, and its `UPDATE` is naturally idempotent, so at-least-once queue delivery cannot corrupt device state. |

---

## 4. Screenshot checklist (insert into the Word document)

1. Resource group `rg-agristream` showing all resources.
2. App Service overview blade (URL, status = Running, plan S1).
3. Deployed web app: landing page, login page, dashboard with data.
4. SQL database Overview + Query Editor showing `SELECT * FROM Users` and
   `DeviceRegistry` rows.
5. Storage account `agristreamst10381088` (Overview blade showing LRS):
   `drone-images` container with blobs; `telemetryd`, `ImageAudit`,
   `IrrigationEvents` tables (Storage Browser); `irrigation-queue` with
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

### 5.1 Disaster Recovery and Redundancy

#### Azure Storage redundancy options

Azure Storage keeps multiple copies of data so that it survives hardware
failures, outages and, depending on the option chosen, regional disasters
(Microsoft, n.d.a). The options differ in where the copies are held.

**Locally Redundant Storage (LRS)** copies data three times synchronously
within a single datacentre in the primary region. It protects against disk,
server and rack failures and offers at least 99.999999999 % (eleven nines)
durability over a year (Microsoft, n.d.a). It does not protect against a
datacentre-level disaster such as fire, flood or a prolonged power failure,
because all three copies sit in the same facility.

**Geo-Redundant Storage (GRS)** applies LRS in the primary region, then copies
the data asynchronously to a paired secondary region hundreds of kilometres
away, where it is again stored with LRS — six copies in total and at least
99.99999999999999 % (sixteen nines) durability (Microsoft, n.d.a). The data
survives the loss of the entire primary region. The secondary copy is only
readable after a failover, unless the account uses Read-Access GRS (RA-GRS).

#### Comparison

| Criterion | LRS | GRS |
|---|---|---|
| Copies of data | 3 (one datacentre) | 6 (3 in the primary region, 3 in the secondary region) |
| Replication to secondary region | None | Asynchronous |
| Protection against | Disk, server and rack failure | All LRS failures plus a full regional outage |
| Durability (per year) | At least 11 nines | At least 16 nines |
| Cost | Lowest | Higher — extra storage plus cross-region transfer |
| Recovery Point Objective (RPO) | Not applicable to a regional disaster, since the data is lost | Not zero — replication is asynchronous, so the latest writes may be lost on failover (Microsoft, n.d.b) |
| Suited to | Replaceable or short-lived data | Irreplaceable or compliance-critical data |

#### Recommendation for AgriStream

- **Critical historical crop-harvest records → GRS (or RA-GRS).** Harvest
  records are irreplaceable and have long-term business value: they support
  yield analysis, agronomic decisions, traceability and possibly regulatory or
  insurance obligations. If a datacentre or region were lost, a farm could lose
  years of history that cannot be regenerated; the extra cost of GRS is small
  compared with that impact. RA-GRS additionally keeps the records readable for
  reporting during a regional outage. Redundancy alone is not a backup —
  corruption or accidental deletion replicates to every copy — so it should be
  combined with soft delete, blob versioning and, for compliance records,
  immutable storage policies (Microsoft, n.d.a).
- **Temporary sensor logs → LRS.** High-frequency soil-sensor readings are
  produced in very large volumes and lose value quickly once the irrigation
  decision has been made; many can simply be re-sent by the devices. Paying for
  geo-replication on millions of low-value rows is not cost-effective, and LRS
  has lower replication overhead, which suits write-heavy telemetry in Table
  Storage. A retention policy can move the data that matters (for example daily
  aggregates) into the GRS-protected store.

**Trade-offs and limitations.** GRS is not free protection: it costs more
because data is stored six times and copied between regions (Microsoft, n.d.a),
and its asynchronous replication means the secondary copy can lag the primary —
Microsoft describes a typical lag of under 15 minutes but gives no SLA on
replication time, so the newest writes can be lost if a failover is forced
(Microsoft, n.d.b). This is the general trade-off of asynchronous replication,
which keeps writes fast and available but accepts possible data loss on
failover; synchronous replication avoids that loss at the price of slower
writes (Kleppmann, 2017). Microsoft now also recommends geo-zone-redundant
storage (GZRS) where available for protection against both zone and regional
failures, and the redundancy setting applies to the whole storage account, so
data with different needs should be separated into different accounts
(Microsoft, n.d.a).

**Decision for this project.** I deployed a single Standard general-purpose v2
storage account, `agristreamst10381088`, with **LRS** redundancy (see Figure
[X], storage account Overview blade). LRS keeps the cost of a development
deployment low while still protecting against disk, server and rack failures,
and the telemetry tables (`telemetryd`, `ImageAudit`, `IrrigationEvents`) plus
the `irrigation-queue` all hold transient data that fits LRS well. In a
production deployment I would split the data across two accounts, because
redundancy is set per storage account: transient telemetry and queue traffic
would stay on LRS, while historical harvest records and evidential drone
imagery would move to a separate GRS (or RA-GRS) account as recommended above.

### 5.2 Stateless vs Stateful Architecture

#### Definitions

A **stateless** component keeps no client-specific data between requests: every
request carries, or can look up, everything needed to process it, so any
instance can serve any request. A **stateful** component remembers earlier
interactions in its own memory or disk, so later requests depend on reaching
the same instance (Wiggins, 2017).

#### Why statelessness is crucial for horizontal scaling

Horizontal scaling (scale-out) adds more instances behind a load balancer
instead of making one instance bigger (Erl, Puttini and Mahmood, 2013). The
benefits hold only if the instances are interchangeable:

1. **Any instance can handle any request** — the load balancer can spread
   traffic evenly without knowing which instance holds which user's data
   (Microsoft, n.d.c).
2. **Instances can be added and removed freely** — autoscale creates instances
   when CPU rises and destroys them when it falls, and a stateless instance can
   be discarded without losing anything (Microsoft, n.d.d).
3. **Resilience** — if an instance crashes or is recycled during a platform
   update, the next request simply goes to another instance with no user
   impact.
4. **Elastic serverless execution** — Azure Functions scales out automatically
   based on trigger load (queue length, blob events). Each execution may run on
   a different instance, so a function cannot rely on data left behind by an
   earlier execution (Microsoft, n.d.e).

#### How local session data and files harm scale-out

- **In-memory session state.** If a login or "farm selection" is stored in the
  memory of one App Service instance, a request routed to a second instance
  finds no session — the user appears logged out or loses their field context.
  The usual workaround, session affinity (ARR "sticky sessions"), creates new
  problems: load becomes uneven because heavy users stay pinned to one instance
  while new instances sit idle after a scale-out; scale-in or a restart
  destroys the sessions held on the removed instance; and failover is
  unreliable because the user's state dies with the instance.
- **Files on the instance's local disk.** If a drone image uploaded through the
  MVC app were saved to the web server's local or temporary disk, other
  instances could not see the file, so behaviour would differ per instance; the
  blob-triggered function would never fire because the image never reached Blob
  Storage; temporary local storage is not durable and is lost on restart,
  redeploy or scale-in (exactly the data loss the brief prohibits); and local
  disk space is limited per instance, capping throughput during large uploads.
- **Authentication keys.** ASP.NET Core encrypts authentication cookies using
  Data Protection keys; if each instance generated its own keys, a cookie
  issued by instance A could not be read by instance B. On App Service the
  platform automatically persists the keys to shared storage
  (`%HOME%\ASP.NET\DataProtection-Keys`), so all instances can decrypt cookies
  — but in other hosting environments the keys must be externalised manually
  (e.g. to Blob Storage or Key Vault).

#### How the AgriStream design keeps state external

| State | Where it is held in my solution | Evidence |
|---|---|---|
| User accounts and roles | Azure SQL `Users` table on `agristream111.database.windows.net` (seeded `admin`, `farmer`, `agronomist`); device status in `DeviceRegistry` | Figure [X] |
| Drone images | Azure Blob Storage, container `drone-images` | Figure [X] |
| Telemetry and audit entries | Azure Table Storage: `telemetryd` (sensor readings), `ImageAudit` (upload audits), `IrrigationEvents` (command events) | Figure [X] |
| Irrigation commands | Azure Storage Queue `irrigation-queue` | Figure [X] |
| Login / session state | **Cookie authentication only — no server-side session state.** `Program.cs` registers `AddAuthentication().AddCookie()` (8-hour expiry) and never calls `AddSession`/`UseSession`. Identity travels inside the encrypted cookie, so any instance can serve any request; on App Service the Data Protection keys are synchronised across instances automatically, so no ARR affinity is required. | `Program.cs`, Figure [X] |

**Scale-out evidence.** During the load test (`scripts/LoadTest.ps1`, Figure
[X]) the CPU autoscale rule scaled the App Service plan from 1 to `<n>`
instances (scale-out at CPU > 70 % for 5 min; Figure [Y], autoscale Run
history). Signed-in users remained signed in across the scale-out because
authentication state lives in the encrypted cookie rather than instance
memory, and `<no / N>` failed requests were observed (Figure [Z]). Instances
scaled back to 1 once CPU fell below 30 %.

**Trade-offs.** Externalising state is not free: every request that reads
session or user data now makes a network call to SQL or Storage, which adds
latency compared with local memory, and the shared services themselves become
potential bottlenecks that must be scaled and made redundant (Kleppmann, 2017).
The benefit is elasticity — capacity follows demand and is paid for only while
it is used (Armbrust et al., 2010). For AgriStream, whose load surges during
peak daylight hours, that trade is worth making.

### 5.3 Serverless Triggers and Bindings

#### Comparison

| Concept | Definition | Purpose | Example in the AgriStream pipeline |
|---|---|---|---|
| Trigger | The event that causes a function to run. Every function has exactly one trigger, and it can supply data to the function (Microsoft, n.d.f). | Decides *when and why* the function executes: a new blob, an HTTP request or a queue message. | One trigger per function: `BlobTrigger("drone-images/{name}")` in `DroneImageAuditFunction`, `HttpTrigger` GET (`/api/telemetry`) in `GetTelemetryFunction`, `HttpTrigger` POST (`/api/irrigation`) in `QueueIrrigationFunction`, and `QueueTrigger("irrigation-queue")` in `ProcessIrrigationFunction` (Figure [X]). The trigger also carries the input payload — the blob `Stream` and `name`, the HTTP request, and the queue `message` string. |
| Input binding | A declarative connection that reads data from a service into the function as a parameter, without manual connection code (Microsoft, n.d.f). | Supplies extra data the function needs while it runs; optional. | **Not used.** Where a function needs extra data it reads it through the Azure SDK via the shared `Env` helper — `TableClient.QueryAsync` with a dynamic `PartitionKey` filter and paging in `GetTelemetryFunction`, and `BlobClient.GetPropertiesAsync` in `DroneImageAuditFunction`. A declarative input binding (e.g. `[TableInput]`) could not express the dynamic filter and paging these reads require. |
| Output binding | A declarative connection that sends the function's result to a service (Microsoft, n.d.f). | Writes results to another service; optional — a function can have several. | **Not used.** Results are written through the SDK: `TableClient.AddEntityAsync` writes the audit row in `DroneImageAuditFunction` and the event row in `ProcessIrrigationFunction`; `QueueClient.SendMessageAsync` places the command in `QueueIrrigationFunction`; and `ProcessIrrigationFunction` updates `dbo.DeviceRegistry` with a parameterised `Microsoft.Data.SqlClient` `UPDATE`, which also lets the code check rows-affected and log unknown devices. The declarative equivalents would be `[TableOutput]` / `[QueueOutput]` attributes. |

#### Discussion

A trigger and a binding differ in what they are for: a trigger *starts* the
function — and a function cannot exist without one — while bindings are
optional helpers that *move data in or out* once it has started
(Microsoft, n.d.f). Bindings use the direction `in` or `out`; a trigger behaves
like an input because it also passes its payload (blob content, queue message
text) into the function.

Where bindings are used, they offer three advantages:

- **Less code and fewer errors** — no hand-written clients, connection logic or
  retry handling per service.
- **Configuration instead of hard-coding** — connection settings live in app
  settings, supporting stateless, portable deployments.
- **Scalability** — the platform manages the trigger listeners, so Functions
  can scale out as queue length or blob events grow (Microsoft, n.d.e).

Bindings suit simple reads and writes but not every case; operations that need
transactions, complex queries or custom error handling are better done with the
service SDK. This is the case throughout the AgriStream functions — all service
access uses the Azure SDK via the shared `Env` helper, most importantly the
parameterised `UPDATE` against Azure SQL in `ProcessIrrigationFunction`, where
the code checks rows-affected and logs a warning for unknown device IDs. Queue
triggers also deliver messages **at least once**, and a message that keeps
failing is eventually moved to a poison queue (Microsoft, n.d.g). AgriStream
handles this in two ways: `ProcessIrrigationFunction` discards malformed
messages after logging (it also defensively Base64-decodes the payload, since
Azurite requires Base64 encoding), and its `UPDATE ... SET Status='Irrigating'`
statement is naturally idempotent — a redelivered message simply rewrites the
same status, so at-least-once delivery cannot corrupt the device registry.

---

## 6. Source code layout

```
AgriStream.sln
├── AgriStream.Web/            # ASP.NET Core 8 MVC application (App Service)
│   ├── Controllers/           # Account, Dashboard, Telemetry, Media, Irrigation, Admin
│   ├── Services/              # SqlService, StorageService, FunctionsClient, PasswordHasher
│   ├── Models/  Views/  Options/
│   ├── wwwroot/css/site.css   # app theme (sidebar shell, parchment/navy/amber palette)
│   └── Program.cs             # cookie auth, DI, App Insights, SQL schema auto-init
├── AgriStream.Functions/      # .NET 8 isolated Azure Functions
│   ├── Functions/             # DroneImageAudit, GetTelemetry, QueueIrrigation, ProcessIrrigation
│   └── Support/Env.cs         # shared SDK client factory (Table/Queue/SQL)
├── sql/schema.sql             # Azure SQL schema + seed
├── scripts/LoadTest.ps1       # concurrent load generator (task e)
├── scripts/Send-Telemetry.ps1 # simulated sensor fleet
└── docs/                      # DEPLOYMENT.md + this report
```

---

## Reference List

Armbrust, M., Fox, A., Griffith, R., Joseph, A.D., Katz, R., Konwinski, A.,
Lee, G., Patterson, D., Rabkin, A., Stoica, I. and Zaharia, M. (2010) 'A view
of cloud computing', *Communications of the ACM*, 53(4), pp. 50–58. Available
at: https://doi.org/10.1145/1721654.1721672 (Accessed: 6 November 2026).

Erl, T., Puttini, R. and Mahmood, Z. (2013) *Cloud computing: concepts,
technology & architecture*. Upper Saddle River, NJ: Prentice Hall.

Kleppmann, M. (2017) *Designing data-intensive applications: the big ideas
behind reliable, scalable, and maintainable systems*. Sebastopol, CA: O'Reilly
Media.

Microsoft (n.d.a) *Azure Storage redundancy*. Available at:
https://learn.microsoft.com/en-us/azure/storage/common/storage-redundancy
(Accessed: 6 November 2026).

Microsoft (n.d.b) *Disaster recovery and storage account failover*. Available
at: https://learn.microsoft.com/en-us/azure/storage/common/storage-disaster-recovery-guidance
(Accessed: 6 November 2026).

Microsoft (n.d.c) *Best practices for cloud applications*. Available at:
https://learn.microsoft.com/en-us/azure/architecture/best-practices/index-best-practices
(Accessed: 6 November 2026).

Microsoft (n.d.d) *Best practices for autoscale*. Available at:
https://learn.microsoft.com/en-us/azure/azure-monitor/autoscale/autoscale-best-practices
(Accessed: 6 November 2026).

Microsoft (n.d.e) *Azure Functions scale and hosting*. Available at:
https://learn.microsoft.com/en-us/azure/azure-functions/functions-scale
(Accessed: 6 November 2026).

Microsoft (n.d.f) *Azure Functions triggers and bindings concepts*. Available
at: https://learn.microsoft.com/en-us/azure/azure-functions/functions-triggers-bindings
(Accessed: 6 November 2026).

Microsoft (n.d.g) *Azure Queue storage trigger and bindings for Azure
Functions overview*. Available at:
https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-storage-queue
(Accessed: 6 November 2026).

Wiggins, A. (2017) *The Twelve-Factor App: VI. Processes*. Available at:
https://12factor.net/processes (Accessed: 6 November 2026).
