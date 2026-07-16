# EGG9000 - Agent Instructions

Discord bot + web dashboard for co-op contracts in *Egg Inc*. Players register Egg Inc accounts, the system fetches backups from the Egg Inc API, groups players into co-ops, tracks status, manages leaderboards, sends notifications. Single-instance Docker deploy (bot, site, RabbitMQ) with MassTransit for inter-service coordination.

All projects target **net10.0**, `LangVersion: preview`. SDK pinned via root `global.json`.

Module docs live in `/docs` (README, bot, common, site, deployment, dev-setup, ci). Keep them in sync with structural changes.

## Solution projects

| Project | Type | Purpose |
|---|---|---|
| `EGG9000.Bot` | Console | Discord bot: slash commands, background jobs, co-op management. DI in `BotHostFactory.cs`. |
| `EGG9000.Site` | ASP.NET Core | Dashboard: co-op views, leaderboards, admin, donations. |
| `EGG9000.Common` | Class lib | EF Core, entities, Egg Inc API client, helpers, services. |
| `EGG9000.Test` | MSTest | Unit tests (`TestCategory=Unit`). |
| `EGG9000.Test.Integration` | MSTest | Testcontainers Postgres: migrations, model drift, DI wiring (`Integration`); live API canary (`Network`). |

## Build / run / test

```bash
dotnet run --project EGG9000.Bot
dotnet watch --project EGG9000.Site --no-hot-reload
dotnet test EGG9000.Test
dotnet run --arch x64 --os linux --project EGG9000.Bot
```

Build configs: `Debug`, `Release`, `DEV9001`, `DEV9002`. See `docs/dev-setup.md`.

Batch verification: make related changes, then build/test once. CI is `.github/workflows/ci.yml` (required gate) plus dependency-review, secret-scan, api-canary, bot-smoke. See `docs/ci.md`.

## Bot command pattern

- Commands are Discord.NET `InteractionService` modules deriving `Interactions/E9KModuleBase.cs` (per-command `Db` created in `BeforeExecuteAsync`).
- Registration and routing: `Services/InteractionRoutingService.cs`. Commands register globally only; stale guild-scoped commands are purged.
- Staff gating: `Interactions/StaffGate.cs` (`[StaffOnly]` + `StaffTier`). Build gating: `Interactions/BuildConfigGate.cs`.
- Services are constructor-injected into modules; respond via `Context.Interaction` and `InteractionExtensions` helpers.
- Command migrations must preserve response semantics exactly: ephemeral flags, defers, visibility.

## Database rules

- PostgreSQL via Npgsql EF Core provider. Connection strings are Npgsql keyword format (`Host=...;Port=5432;...`).
- Migrations are **append-only** in `EGG9000.Common/Migrations/` (single dir; Site points `MigrationsAssembly` at Common). Never edit an existing migration.
- Migrations auto-apply on startup in Release only. Dev configs apply manually.
- `DateTimeOffset.Now` is rejected by Npgsql in LINQ queries; UTC conversion is handled by `UtcDateTimeOffsetConverter`. Use UTC.
- Storage conventions, no third format: MessagePack+LZ4 for `DBUser` collections, GZip JSON for coop status blobs, JSON strings for contract goals/rewards and guild settings.

## Serialization gotchas

- `EggIncAccount` is MessagePack-serialized with explicit `[Key(n)]` indices. Never renumber or repurpose keys; new fields append with the next index and must tolerate defaults, or stored data corrupts.
- GZip coop-status blobs are shape-fragile. Changing the status type can silently fail deserialization of old blobs.

## Egg Inc API

- Protobuf over HTTP, not gRPC: base64 protobuf POST to `https://www.auxbrain.com/`. Schemas in `EGG9000.Common/Proto/` (`ei.proto`, `common.proto`); C# types are generated at build by `Grpc.Tools` into `obj/`.
- Some endpoints need an authenticated wrapper. The salt comes from a runtime secret (`egg_inc_api_salt` Docker secret / `ConnectionStrings:ApiSalt`), never source. `EggIncApiSecrets.IsSaltAvailable` gates those endpoints.
- `AppVersion` / `AppBuild` / `ClientVersion` must track the live game client. Stale values get requests rejected.

## Other rules

- Register services in `BotHostFactory.cs` (Bot) / `Program.cs` (Site). Constructor injection, no static state.
- EI ID screenshot reading (`EIIDScreenShots`) glyph-matches against `EGG9000.Bot/Fonts/always together.otf`; the font must ship with the bot.
- Match the style of surrounding code. No dead code, no decorative formatting.
- Never commit secrets, credentials, internal hostnames, or tokens. Secrets flow through Docker secrets or user secrets only.
