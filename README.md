# EGG9000

Discord bot and web dashboard for managing co-op contracts in the mobile game *Egg Inc*. Players register their Egg Inc accounts; the system fetches backup data from the Egg Inc API, groups players into co-ops, tracks progress in Discord threads, manages leaderboards, and sends notifications.

## Components

| Project | Purpose |
|---|---|
| `EGG9000.Bot` | Discord slash-command bot and background jobs |
| `EGG9000.Site` | ASP.NET Core MVC dashboard |
| `EGG9000.Common` | Shared library: EF Core entities, Egg Inc API client, helpers |
| `EGG9000.Test` | Unit tests |
| `EGG9000.Test.Integration` | Integration tests (Testcontainers PostgreSQL) |

Stack: .NET 10, Discord.NET, EF Core + PostgreSQL, MassTransit + RabbitMQ, Docker Compose.

## Quick start

```
dotnet run --project EGG9000.Bot
dotnet watch --project EGG9000.Site --no-hot-reload
dotnet test EGG9000.Test
```

SDK version is pinned in `global.json`. Full local setup, build configurations, secrets, and the dev Docker stack: [docs/dev-setup.md](docs/dev-setup.md).

## Documentation

| Doc | Contents |
|---|---|
| [docs/README.md](docs/README.md) | System overview and architecture |
| [docs/bot.md](docs/bot.md) | Commands, jobs, services |
| [docs/common.md](docs/common.md) | Database, entities, Egg Inc API client |
| [docs/site.md](docs/site.md) | Controllers, auth model, metrics |
| [docs/deployment.md](docs/deployment.md) | Docker Compose stack and secrets |
| [docs/ci.md](docs/ci.md) | GitHub Actions workflows |

Agent/contributor instructions: [AGENTS.md](AGENTS.md).
