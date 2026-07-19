# EGG9000 - Developer Setup

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 10.0.100 (`global.json`, `rollForward: latestFeature`) | Build and run all projects. |
| Docker | recent | Integration tests (Testcontainers) and the dev compose stack. |
| PostgreSQL | 16+ | Local database for Debug runs. Integration tests spin up their own container. |

All projects target `net10.0` with `LangVersion: preview`.

## Build configurations

| Config | Use | Notes |
|---|---|---|
| `Debug` | Local development | Manual migrations. Enables `/Home/DebugLogin`. |
| `Release` | Production | Auto-applies EF migrations on startup. |
| `DEV9001` | Dev instance A | Runs against the live DB. Migrations stay manual. |
| `DEV9002` | Dev instance B | Runs against the live DB. Migrations stay manual. Enables `/Home/DebugLogin`. |

Caution: DEV9001/DEV9002 point at live data. Only Release ever auto-migrates.

### User secrets IDs per configuration

The `UserSecretsId` differs by project and configuration (from the csproj files):

| Config | Bot | Site |
|---|---|---|
| `Debug` / `Release` | `dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a` | same |
| `DEV9001` | `DEV9001-LiveDB` | `dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a` |
| `DEV9002` | `DEV9001` | `DEV9001` |

## Local configuration

Credentials load from .NET user secrets (`%APPDATA%\Microsoft\UserSecrets\{id}\secrets.json` on Windows). `SecretsHelper` prefers Docker secrets at `/run/secrets/` when present and falls back to configuration, so the same keys work locally and in containers.

Set each key against the ID for your configuration (Debug shown):

```powershell
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:DefaultConnection" "Host=<pg-host>;Port=5432;Database=EGG9000;Username=<user>;Password=<password>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:Token" "<discord-bot-token>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:ClientId" "<discord-client-id>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:ClientSecret" "<discord-client-secret>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:BugSnagApiKey" "<bugsnag-key>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:RabbitMQServer" "localhost|<user>|<password>"
dotnet user-secrets --id dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a set "ConnectionStrings:ApiSalt" "<egg-inc-api-passphrase>"
```

Notes:

- `DefaultConnection` is Npgsql keyword format, not a SQL Server string.
- `RabbitMQServer` format is `host|user|password`.
- `ApiSalt` is optional. Without it, only the authenticated Egg Inc endpoint degrades (logged once); registration and backup fetching use the salt-free endpoint.
- `.gitignore` excludes `**/secrets.json`, `**/.env`, `**/.env.*`, `**/appsettings.Local.json`, and `**/appsettings.*.local.json`, so local credential files never land in git. User secrets are the mechanism the apps actually load.
- There is no `docker-compose.override.yml` in this repo.

## Running locally

```powershell
dotnet run --project EGG9000.Bot
dotnet watch --project EGG9000.Site --no-hot-reload
```

The site listens on `http://0.0.0.0:5013`.

### Debug login (site)

`/Home/DebugLogin?id={discordId}` signs you in without Discord OAuth. It returns 404 unless the build is `Debug` or `DEV9002`; the Discord ID must exist as a registered user in the main guild and already have an Identity login row (a prior real OAuth sign-in). Local development only.

## Tests

```powershell
dotnet test EGG9000.Test --filter "TestCategory=Unit"
dotnet test EGG9000.Test.Integration --filter "TestCategory=Integration"
dotnet test EGG9000.Test.Integration --filter "TestCategory=Network"
```

| Category | Project | Needs |
|---|---|---|
| `Unit` | `EGG9000.Test` | Nothing external. |
| `Integration` | `EGG9000.Test.Integration` | Docker. Testcontainers starts a disposable Postgres; covers migration apply, model-drift guard, DI wiring. |
| `Network` | `EGG9000.Test.Integration` | Live Egg Inc API access (canary tests; advisory in CI). |

CI (`.github/workflows/ci.yml`) runs `Unit` and `Integration` with these same filters.

## Dev compose stack

```powershell
docker-compose -f docker-compose.dev.yml up
```

Runs bot + site + rabbitmq, one instance each. What it does:

- Builds both images from the repo Dockerfiles with `BUILD_CONFIGURATION: Debug`.
- Sets `DOTNET_ENVIRONMENT=Development` and mounts `%APPDATA%\Microsoft\UserSecrets` read-only into the containers, so your user secrets work inside Docker.
- RabbitMQ runs with hardcoded dev credentials `e9k` / `devpassword`. These are local-only defaults for the throwaway dev broker; production credentials are env-indirected in `docker-compose.yml`.
- Site is on `http://localhost:5013`, RabbitMQ management UI on `http://localhost:15672`.

## Database migrations

Append-only, single directory `EGG9000.Common/Migrations/`. Never edit an existing migration.

```powershell
dotnet ef migrations add <Name> --project EGG9000.Common --startup-project EGG9000.Bot
dotnet ef database update --project EGG9000.Common --startup-project EGG9000.Bot
```

Migrations auto-apply on startup in Release builds only. In Debug/DEV9001/DEV9002 you apply them manually with `dotnet ef database update`.

## OCR asset

Screenshot ID reading (`EIIDScreenShots`) matches glyphs rendered from `EGG9000.Bot/Fonts/always together.otf`, which the Bot csproj copies to the output directory. If the font is missing, OCR fails; there is no Tesseract/tessdata dependency on this branch.

## Health endpoints (site)

| Endpoint | Purpose |
|---|---|
| `GET /health` | Liveness, returns 200 JSON. |
| `GET /home/alive` | DB connectivity probe. |
| `GET /home/alivediscord` | Discord gateway state, 200 or 503. |
