# EGG9000 - Documentation

Living documentation for the EGG9000 system. Start here, then follow links into the module docs.

## What is EGG9000?

EGG9000 is a Discord bot and web dashboard for managing co-op contracts in the mobile game *Egg Inc*. Players register their Egg Inc account IDs; the system fetches their backup data from the Egg Inc API, groups them into co-ops, tracks co-op progress in Discord threads, manages leaderboards, sends notifications, and provides a web dashboard.

The system serves multiple Discord guilds. Each guild has its own configuration, channel layout, staff roles, and customizations.

## System components

| Component | Description | Doc |
|---|---|---|
| **EGG9000.Bot** | Discord slash-command bot. Co-ops, merits, artifacts, player info. Background jobs sync data from the Egg Inc API. | [bot.md](./bot.md) |
| **EGG9000.Site** | ASP.NET Core MVC dashboard. Leaderboards, co-op views, farm views, admin tools, donations. | [site.md](./site.md) |
| **EGG9000.Common** | Shared class library. EF Core entities, PostgreSQL context, Egg Inc API client, MassTransit consumers, game-logic helpers. | [common.md](./common.md) |
| **EGG9000.APILinkSite** | Legacy API link endpoint. Being phased out; no new dependencies. | (no doc) |
| **EGG9000.Test** | MSTest unit tests (`TestCategory=Unit`). | (no doc) |
| **EGG9000.Test.Integration** | Testcontainers Postgres integration tests plus live API canary (`Integration` / `Network`). | [ci.md](./ci.md) |

## Architecture

```
Discord users
      |
      | slash commands / events
      v
EGG9000.Bot
      |           |
      |           | MassTransit / RabbitMQ
      |           v
      |     EGG9000.Site <----> Browser / Discord OAuth
      |           |
      +---------->+
                  |
            EGG9000.Common
                  |
          +-------+--------+
          |                |
    PostgreSQL         Egg Inc API
                    (auxbrain.com)
```

- Bot and site share one PostgreSQL database via `EGG9000.Common`'s `ApplicationDbContext`.
- Bot and site coordinate over RabbitMQ (MassTransit): cache expiry, restart/shutdown signals, API version updates, bot metrics snapshots.
- The bot polls the Egg Inc API on a schedule; the site does not call it directly in normal operation.
- Single instance of each service. Promotion is redeploy + restart. See [deployment.md](./deployment.md).

## Technology stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10.0, C# preview language features |
| Bot library | Discord.NET 3.20 (`InteractionService` modules) |
| Web framework | ASP.NET Core 10 MVC + Razor Pages |
| ORM | EF Core 10 with Npgsql (PostgreSQL) |
| Messaging | MassTransit 8.5 with RabbitMQ 3.12 |
| Game API | Protobuf over HTTP (auxbrain.com) |
| Blob serialization | MessagePack + LZ4; GZip for co-op status payloads |
| Auth | Discord OAuth2 + ASP.NET Identity |
| Payments | Stripe.Net |
| Metrics | prometheus-net (`/metrics`, admin-gated) |
| Error tracking | Bugsnag |
| Logging | NLog |
| Scheduling | Cronos cron expressions + periodic updaters |
| Deployment | Docker Compose, single instance per service |

## Database

All persistent state lives in one PostgreSQL database. Key tables:

| Table | Purpose |
|---|---|
| `DBUsers` | Discord users with linked Egg Inc accounts |
| `Guilds` | Per-server configuration |
| `Contracts` | Egg Inc contract definitions |
| `Coops` | Individual co-op runs |
| `UserCoopXrefs` | User-to-coop membership |
| `GuildContracts` | Contract settings per guild |
| `UserSnapShots` | Daily leaderboard history |
| `GlobalLeaderboardUsers` | Global player rankings |
| `AutomationLogs` | Audit trail for scheduled jobs |
| `ApiKeys` | Hashed API keys for the JSON leaderboard endpoint |

See [common.md](./common.md) for all entities and storage patterns.

**Migrations are append-only** in `EGG9000.Common/Migrations/`. Never modify an existing migration. Release builds auto-apply migrations at startup.

## Egg Inc API

The bot talks to the Egg Inc backend at `https://www.auxbrain.com/`. Requests are protobuf-encoded, base64-wrapped, POSTed as form data. Some endpoints require an authenticated wrapper whose salt comes from a runtime secret (`egg_inc_api_salt` / `ConnectionStrings:ApiSalt`); without it those endpoints are disabled.

`AppVersion`, `AppBuild`, `ClientVersion` in `EggIncApi.cs` must track the live game client. Stale values cause API rejections.

See [common.md](./common.md) for the full endpoint table.

## Bot summary

Slash commands are Discord.NET `InteractionService` modules deriving `E9KModuleBase`, routed by `InteractionRoutingService`, registered globally. Major groups: registration, co-op management, coop/contract settings, merits/demerits, staff (`/a`, `/admin`, `/bot`, `/b`), informational (artifacts, prestige, crafting, formulas), misc (FAQ, APOD). Roughly 20 background updaters handle backups, contracts, threads, leaderboards, events, and notifications.

See [bot.md](./bot.md) for command tables and job schedules.

## Module docs

| Doc | Contents |
|---|---|
| [bot.md](./bot.md) | Slash commands, automated jobs, services, command pattern |
| [common.md](./common.md) | Database schema, entities, Egg Inc API client, helpers |
| [site.md](./site.md) | Controllers, auth model, services, metrics |
| [deployment.md](./deployment.md) | Docker Compose stack, secrets, migrations, health |
| [dev-setup.md](./dev-setup.md) | Local development setup |
| [ci.md](./ci.md) | GitHub Actions workflows |
