# EGG9000.Site

ASP.NET Core web dashboard for the EGG9000 ecosystem: co-op views, leaderboards, farm dashboards, staff/admin tooling, and Stripe donations. Shares the database and message bus with `EGG9000.Bot`.

Binds `http://0.0.0.0:5013` (set in `Program.cs`; the compose file also sets `ASPNETCORE_URLS` to the same port).

## Technology stack

| Concern | Technology |
|---|---|
| Framework | ASP.NET Core, net10.0, MVC + Razor Pages, `LangVersion: preview` |
| Database | PostgreSQL via Npgsql EF Core; context and migrations live in `EGG9000.Common` |
| Auth | Discord OAuth2 (AspNet.Security.OAuth.Discord) + ASP.NET Core Identity |
| API auth | Custom `X-Api-Key` scheme (`Auth/ApiKeyAuthenticationHandler.cs`) |
| Payments | Stripe.Net |
| Messaging | MassTransit over RabbitMQ (Release; mock publish endpoint otherwise) |
| Metrics | prometheus-net (`/metrics`) |
| Imaging | SixLabors.ImageSharp + ImageSharp.Drawing |
| Logging | NLog; Bugsnag error reporting in Release |

`Docker.DotNet` is still referenced in the csproj but nothing uses it; it is removable.

## Startup (`Program.cs`)

1. NLog setup, `SecretsHelper.Initialize` (config keys or Docker secrets).
2. Service registration (see below), Kestrel on port 5013.
3. Release only: `Database.Migrate()` on the shared `ApplicationDbContext`. Dev configs migrate manually. EF's advisory lock serializes concurrent applies with the bot.
4. Ensures the `GuildReadOnlyAdmin` IdentityRole row exists (idempotent). Role assignment itself is manual, via the admin Permissions UI.
5. Middleware pipeline, route mapping, `app.Run()`.

### Middleware pipeline (order matters)

1. `UseForwardedHeaders` - before anything that reads scheme/host, so the Discord OAuth `redirect_uri` is built as https behind the TLS-terminating proxy.
2. Security response headers on every response: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, and a report-only CSP (allows self, inline script/style, Stripe JS/API, https images).
3. Exception handler + HSTS (non-development).
4. HTTPS redirection, static files (`ServeUnknownFileTypes = true`).
5. Routing, `UseHttpMetrics` (per-request Prometheus HTTP metrics), response caching, authentication, authorization.
6. Static assets, controller routes (`/invite`, `/coop/{ContractId}/{CoopId}`, default), gated `/metrics`, Razor Pages.

### Trusted proxies

Forwarded headers (`X-Forwarded-For` / `X-Forwarded-Proto`) are honored only from subnets listed in the `TRUSTED_PROXY_NETWORKS` env var (comma-separated CIDR). When unset, a hardcoded fallback subnet in `Program.cs` applies so an un-updated deploy keeps its old behavior.

### Data protection

Keys persist to the database (`PersistKeysToDbContext`), application name `EGG9000`. They are encrypted at rest with a PFX certificate resolved from `DataProtection:CertPath` / `DataProtection:CertPassword` (config or Docker secrets `dataprotection_cert_path` / `dataprotection_cert_password`). If unconfigured, startup logs a warning and keys are stored unencrypted - acceptable for local dev only.

## Authentication

Discord OAuth2 only; no password login.

1. Unauthenticated request redirects to `/Identity/Account/Login`.
2. Login page issues the Discord OAuth challenge; callback lands on `/Identity/Account/ExternalLogin?handler=Callback`.
3. `Data/CustomClaimsPrincipleFactory.cs` looks up the Discord ID in `DBUsers`. Unregistered users get `ErrorAccountNotFound`; registration happens through the bot.
4. Claims added: `DbUserId`, `DiscordId`, `GuildId`, `DarkMode`.

Requests with a `Discordbot` user agent are redirected from the login page to `/Home/Embed?returnUrl=...` so Discord link previews render without auth.

Cookies: `egg9000Cookie` (application) and `egg9000CookieExternal` (external), both 15-day sliding expiration, HttpOnly, `SecurePolicy.Always`.

## Authorization

Deny-by-default: a global `FallbackPolicy` requires an authenticated user for every endpoint that does not opt out. Public endpoints must carry `[AllowAnonymous]`.

| Role | Scope |
|---|---|
| `Admin` | Global operator. Only role that can reach `/metrics`, bot restart, permissions UI. |
| `GuildAdmin` | Per-guild staff, full read+write. |
| `GuildLesserAdmin` | Per-guild staff, read + most writes. |
| `GuildReadOnlyAdmin` | Per-guild staff, read-only tier. |

Additional schemes:

- **API key** (`Auth/ApiKeyAuthenticationHandler.cs`): reads `X-Api-Key`, compares SHA256 hash against the `ApiKeys` table, honors revocation and expiry, logs every request (`ApiKeyRequestLogs`) and increments per-day usage. Successful auth yields `GuildId` / `ApiKeyId` claims. Used by `Home/LeaderboardJson`. Keys are managed in Admin > ApiKeys.
- **Internal bot key**: the `APIController` image endpoints are `[AllowAnonymous]` but in Release require an `authenticationKey` header matching the bot token; mismatches return 404.
- **Stripe webhook**: authenticated solely by `Stripe-Signature` verification (see Donation).

## Controllers

Auth column: `Anonymous` = `[AllowAnonymous]`; `Cookie` = any authenticated user (fallback policy); role lists = `[Authorize(Roles = ...)]`. `Staff` = `Admin,GuildAdmin,GuildLesserAdmin,GuildReadOnlyAdmin`.

### HomeController

| Action | Auth | Purpose |
|---|---|---|
| `Index`, `Privacy`, `Invite`, `Error`, `ClearCookies` | Anonymous | Landing, static pages, cookie reset |
| `Alive`, `AliveDiscord` | Anonymous | DB / Discord-connection liveness probes |
| `Embed` | Anonymous | Discord link-preview shell page |
| `Coop` (`/coop/{ContractId}/{CoopId}`) | Anonymous | Public co-op detail page |
| `Leaderboard`, `EggDayLeaderboard`, `Enlightenment`, `Comparison`, `GradeComparison`, `CraftingLevelComparison` | Cookie | Leaderboards and comparison views |
| `FAQ`, `CSLeaderboard`, `CraftingLevelLeaderboard`, `Results`, `Boosts` | Cookie | FAQ, contract-score/crafting boards, results, active boosts |
| `HalloweenHunt`, `EasterEggHunt` | Cookie | Seasonal hunt pages (response-cached) |
| `LeaderboardXML` | Cookie | XML leaderboard export |
| `LeaderboardJson` | API key scheme | JSON leaderboard for external consumers |
| `XmlOut`, `JsonOut`, `RawJsonOut`, `CustomBackupOut` | Admin,GuildLesserAdmin,GuildAdmin | Raw backup dumps for a given Egg Inc ID |
| `ViewUser(Guid)` | Staff | Redirects to `MyFarms/ViewUser` for the user |
| `ViewUserId(string)`, `ViewBackup` | Admin,GuildAdmin | Ad-hoc backup fetch / backup JSON by user |
| `CheckDiscord`, `UpdateDiscord`, `CleanCoopPins`, `AddAdminRole`, `CheckChannels` | Admin | Global maintenance actions |

### HealthController

`[Route("[controller]")]`, anonymous. `GET /health` returns `{ status: "healthy", timestamp }`. Used by the compose healthcheck.

### ContractController

Class-gated `[Authorize]` (any signed-in user); staff actions elevate.

| Action | Auth | Purpose |
|---|---|---|
| `Index`, `Coop`, `Details`, `CoopStatusJson`, `ScoreGraph`, `RecentScoresGrid`, `Day1CoopsFillLate` | Cookie | Contract list, co-op detail, live status JSON, score graphing |
| `ReloadGrade`, `StartCoops` | Admin,GuildAdmin | Rebuild assignments for a grade; create co-ops from the grid |
| `MoveToCoop`, `RemoveXref` | Admin,GuildAdmin,GuildLesserAdmin | Move a player between co-ops; remove a membership |

### MyFarmsController

Class-gated `[Authorize]`. Data is scoped to the logged-in user unless noted.

| Action | Auth | Purpose |
|---|---|---|
| `Index` | Cookie | Farm dashboard; kicks off a background backup refresh |
| `InventoryOverlay` | Cookie (owner or staff enforced in code) | Lazy JSON: artifact-inventory image (base64) + hover-hotspot manifest |
| `EarningsBoostCalculator`, `ResearchTest`, `SubmitResearchCost` | Cookie | EB calculator, research-cost crowdsourcing |
| `SaveContractSetting`, `TestAssignment` | Cookie | Contract-settings v2: save a setting field; dry-run the assignment engine with diagnostics |
| `Roles` | Cookie | Returns the caller's Identity roles as JSON (drives UI gating) |
| `SendTestDM` | Cookie | Test ping; always targets the authenticated user |
| `ViewUser(discordId)` | Staff | View another user's farms |
| `RemoveDemerit`, `RemoveMerit` | Admin,GuildAdmin | Delete a merit/demerit row |

### AdminController

Class-gated to `Staff` (all four tiers). Sensitive actions elevate per-action:

| Gate | Actions |
|---|---|
| Admin | `RestartBot` (publishes `RestartMessage` on the bus), `LookForLargeJump`, `UserPermissions`, `EditUsers`, `SetRole`, `SetCustomName` |
| Admin,GuildAdmin | `RemoveDemerit`, `ApiKeys`, `CreateApiKey`, `RevokeApiKey`, `ApiKeyRequestLog` |
| Admin,GuildAdmin,GuildLesserAdmin | `LatestDemerits`, `EventCustomization`, `SaveEventCustomization`, `FAQCustomization`, `SaveFAQTopic`, `DeleteFAQTopic`, `RemoveServer` |
| Staff (class gate) | Dashboards and reports: `Index`, `GetGraphs`, `Contract`, `ContractScores`, `CalculateScore`, `ReCalculateRunningScore`, `Slackers`, `Sleepers`, `Ghosts`, `DeleteGhost`, `Leechers`, `SearchID`, `DuplicateChannels`, `Deleteduplicate`, `DeleteAllDuplicates`, `AutomatedTasks`, seasonal hunt admin pages, `ConfigureServer`, `SaveChannelDetails`, `SaveRolesToSync`, `StandardPermit`, `InactivePlayers`, `NonServerUsers`, `SaveNotes`, `Sync`/`DiscordReturn`/`SyncCommandPermissions`, `Guilds`, `CheckCoopCreators`, `AddGuildToDb`, `DeleteOutsideCoopMessage` |

API key management (`ApiKeys` pages) shows keys once at creation, stores only the SHA256 hash, and exposes a per-key request log.

### DonationController

Class `[AllowAnonymous]`.

| Action | Auth | Purpose |
|---|---|---|
| `Index`, `ThankYou` | Anonymous | Donation landing / thank-you pages |
| `Endpoint` (POST) | Stripe signature | Stripe webhook |

Webhook behavior: Stripe API key and webhook signing secret come from config or Docker secrets (`stripe_api_key`, `stripe_webhook_secret`). If either is missing the endpoint returns 404 rather than trusting the body. Payloads are verified with `EventUtility.ConstructEvent` against the `Stripe-Signature` header; failures return 400. Only `checkout.session.completed` is handled: records a `Donation` row (when the session carries a user reference), grants the Donor role, and posts an announcement message.

### APIController

Class `[AllowAnonymous]`; every endpoint Release-gates on the internal `authenticationKey` header (bot token). These are bot-to-site image rendering calls.

| Endpoint (POST) | Purpose |
|---|---|
| `/api/generateeventimage` | Event announcement image (colored/gradient background per event type) |
| `/api/generateinventoryb64` | Artifact inventory JPEG via `ArtifactImageRenderer.RenderInventory` |
| `/api/generateafxsetsb64` | Paginated artifact-set sheets, base64 JPEG pages |
| `/api/generateartifactsetb64` | Single labeled artifact-set row, base64 JPEG |

## Metrics

`/metrics` (prometheus-net) is mapped with `RequireAuthorization(Roles = "Admin")` - Discord cookie auth plus the global Admin role; guild staff tiers are excluded. Exports:

- Default registry: GC/memory/process counters plus per-request HTTP metrics from `UseHttpMetrics`.
- `bot_*` gauges (uptime, working set, GC, gateway latency, send-queue depth, API/DB/command counters, snapshot timestamp) owned by `Services/BotMetricsExporter.cs`. The bot publishes `BotMetricsSnapshotMessage` over the bus every 15s; `Consumers/BotMetricsSnapshotConsumer.cs` validates the bus control secret and applies each snapshot. The consumer binds a per-instance temporary queue so snapshots broadcast rather than load-balance.

## Background services and consumers

| Component | Purpose |
|---|---|
| `Services/NewCoopChecker.cs` | Every 30s counts co-ops waiting on creation/threads; sets a backpressure flag that makes heavy admin graph pages return 503 during creation storms |
| `UserCacheRefreshService`, `ActiveCoopsCacheRefreshService` (Common) | Refresh `DatabaseCache` (users 60s, active co-ops 5min) |
| `ExpireCacheConsumer` (Common) | Bus-triggered cache invalidation |
| `UpdateApiVersionsConsumer` (Common) | Broadcast Egg Inc client-version updates to every running instance (temporary per-instance queue) |
| `Services/EmailSenderBlank.cs` | No-op `IEmailSender` (Identity requires one; the site never sends email) |

## Rendering services and client assets

- `Services/ArtifactImageRenderer.cs` - singleton; `RenderInventory` (inventory sheet + hover-target manifest) and `RenderSet` (a farm's active artifacts) share one `PaintCell` so the bot image endpoint and the MyFarms Inventory tab paint identically. Manifests are percent-coordinate hotspot DTOs (`ArtifactOverlayManifest` in Common).
- `wwwroot/js/artifact-overlay.js` - lays hotspots over rendered images and drives the shared floating rich tooltip (markup in hidden `.afx-tip-content` children of `.has-tip` elements).
- `wwwroot/js/eb-stats-chart.js`, `virtue-stats-chart.js` - EB-history and virtue-stats charts (ApexCharts partial in `Views/Shared/_ApexCharts.cshtml`).

## Views

`Views/Home` (leaderboards, comparisons, co-op page, seasonal hunts), `Views/Contract` (list, details, score graph, recent-scores grid), `Views/MyFarms` (dashboard `Index` plus tab partials: artifact inventory/combos/sets, colleggtibles, contract history, EB history, epic research, ships and farms, virtue stats, contract settings, test assignment), `Views/Admin` (reports, config, API keys, permissions), `Views/Donation`, `Views/Shared` (layout, login partial, ApexCharts). Identity UI lives in `Areas/Identity/Pages/Account` (Discord login, external-login callback, `ErrorAccountNotFound`, access denied, logout).

## Database

- Single shared `ApplicationDbContext` from `EGG9000.Common`; the DbContext factory pins `MigrationsAssembly("EGG9000.Common")`. Migrations are append-only.
- `EnableSensitiveDataLogging` is intentionally on (parameter values needed to debug user issues); oversized log messages are dropped by NLog config instead.
- Connection string `DefaultConnection` in Npgsql keyword format.

## Docker / deployment

- `EGG9000.Site/Dockerfile`: multi-stage build on the .NET 10 SDK/ASP.NET images, runs as the non-root `app` user, installs curl for the healthcheck. No Docker socket mount; the site does not manage containers.
- `docker-compose.yml`: service `site`, port `5013:5013`, healthcheck `curl -f http://localhost:5013/health`.
- Release adds gzip response compression, Bugsnag, and RabbitMQ transport (host/user/pass parsed from the `RabbitMQServer` connection string).
- Secrets (Stripe, Bugsnag, data-protection cert, bot token, API salt) resolve via `SecretsHelper` from config keys or mounted Docker secrets.

## Configurations

`Debug`, `Release`, `DEV9001`, `DEV9002`. Migration auto-apply and the RabbitMQ/Bugsnag/compression block are Release-only; non-Release uses a mock publish endpoint. See the dev-setup doc for local development.
