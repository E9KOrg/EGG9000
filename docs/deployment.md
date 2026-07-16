# EGG9000 Deployment

Single-instance Docker Compose stack on one host: `bot`, `site`, `rabbitmq`. Promotion is redeploy + restart: build a new image, push or stream it to the host, `docker-compose pull && docker-compose up -d`. There is no blue-green, no standby instance, no traffic switching.

## Compose stack (`docker-compose.yml`)

| Service | Image | Ports | Restart | Depends on |
|---|---|---|---|---|
| `bot` | `kendrome/egg9000bot:latest` | none | `on-failure` | `rabbitmq` healthy |
| `site` | `kendrome/egg9000site:latest` | `5013:5013` | `always` | `rabbitmq` healthy |
| `rabbitmq` | `rabbitmq:3.12-management` | `127.0.0.1:5672`, `127.0.0.1:15672` | `unless-stopped` | - |

All three share the bridge network `egg9000_network` and talk by service name. Bot and site log via `json-file`, 10 MB max, 3 files.

Notes verified against the compose file:

- RabbitMQ ports bind to loopback only. AMQP and the management UI are never exposed on `0.0.0.0`. Reach the management UI through an SSH tunnel to `<host>`.
- The Docker socket is not mounted anywhere. The site has no Docker API access.
- Both app containers run as the non-root `app` user from the base image.
- The bot reaches the site over the Docker network via `E9K_SITE_BASEURL=http://site:5013` because the public domain cannot be hairpinned from inside a container.
- `TRUSTED_PROXY_NETWORKS` on the site holds the CIDR(s) of the TLS-terminating reverse proxy so `X-Forwarded-Proto` is trusted. Comma-separated, IPv4 and IPv6.

### RabbitMQ credentials

Credentials are env-indirected, never hardcoded in the production compose file:

```yaml
RABBITMQ_DEFAULT_USER: ${RABBITMQ_DEFAULT_USER:-guest}
RABBITMQ_DEFAULT_PASS: ${RABBITMQ_DEFAULT_PASS:-change_me}
```

Set both in the host environment or a local `.env` (gitignored). The app-side connection string `ConnectionStrings__RabbitMQServer` uses the format `host|user|password` and must match.

### Environment keys

The compose `x-environment` anchor declares the expected `ConnectionStrings__*` keys (`DefaultConnection`, `ClientId`, `Token`, `ClientSecret`, `BugSnagApiKey`, `RabbitMQServer`) with placeholder values. Real values come from Docker secrets at runtime (below) or host-side env overrides. Never commit real values.

## Images (Dockerfiles)

Both are multi-stage: `mcr.microsoft.com/dotnet/aspnet:10.0` base, `mcr.microsoft.com/dotnet/sdk:10.0` build/publish, `USER app`, publish with `/p:UseAppHost=false`, default `BUILD_CONFIGURATION=Release`.

| Dockerfile | Extras |
|---|---|
| `EGG9000.Bot/Dockerfile` | Bakes git metadata (`GIT_MESSAGE`, `GIT_HASH`, `GIT_AUTHOR`, `GIT_TIMESTAMP`, `GIT_BRANCH`, `GIT_REMOTE` build args) into `version.txt` for the bot's version reporting. |
| `EGG9000.Site/Dockerfile` | Installs `curl` in the base stage for the compose healthcheck. `EXPOSE 8080` is vestigial; the site listens on 5013 via `ASPNETCORE_URLS`. |

## Building and publishing (`publish-docker.ps1`)

| Invocation | Behavior |
|---|---|
| `.\publish-docker.ps1` | Build both images locally, tagged `:latest` and `:yyyyMMdd-HHmmss`. |
| `.\publish-docker.ps1 -Bot` / `-Site` | Build only one image. |
| `.\publish-docker.ps1 -Push` | Also push timestamp + latest tags to Docker Hub. |
| `.\publish-docker.ps1 -RemoteHost <host> -RemoteUser <user>` | No registry: `docker save` piped over SSH into `docker load` on the host. |

The bot image collects git metadata (falls back to placeholders off a git checkout) and passes it as build args.

Known quirk: the site branch of the script pushes to Docker Hub unconditionally, ignoring `-Push`/`-RemoteHost`, and recomputes the timestamp before pushing, so the pushed timestamp tag can differ from the tag that was built.

## Secrets (`deploy-secrets-remote.ps1`)

```powershell
.\deploy-secrets-remote.ps1 -RemoteHost <host> -RemoteUser <user>
```

Reads the local user-secrets file (default `%APPDATA%\Microsoft\UserSecrets\DEV9001\secrets.json`), base64-encodes each value for transport, and over SSH runs `docker secret rm` + `docker secret create` per secret on the remote host. It verifies SSH and Docker availability first and lists secrets afterward.

| Secret | Purpose |
|---|---|
| `db_connection_string` | PostgreSQL connection string, Npgsql keyword format (`Host=...;Port=5432;Database=...;Username=...;Password=...`). |
| `discord_client_id` | Discord OAuth application ID. |
| `discord_token` | Discord bot gateway token. |
| `discord_client_secret` | Discord OAuth client secret. |
| `bugsnag_api_key` | Bugsnag error tracking. |
| `rabbitmq_connection` | `host|user|password` broker connection. |
| `egg_inc_api_salt` | Egg Inc API auth passphrase. Optional: skipped when `ConnectionStrings:ApiSalt` is unset locally; authenticated Egg Inc endpoints degrade gracefully without it. |

At runtime `SecretsHelper` (EGG9000.Common) reads `/run/secrets/<name>` first and falls back to configuration keys, so the same code runs with Docker secrets in production and user secrets locally. Note: `docker-compose.yml` declares no `secrets:` block; making the secrets visible at `/run/secrets/` depends on the host's Docker setup.

Never write secret values into compose files, docs, or source.

## Database migrations

- Migrations live in a single directory: `EGG9000.Common/Migrations/`. Append-only; never edit an existing migration.
- Both bot and site call `Database.Migrate()` on startup in Release builds only. Dev configs (Debug, DEV9001, DEV9002) stay manual so a half-written migration cannot auto-apply to the live DB.
- Both services migrate the one shared `ApplicationDbContext`; EF's advisory lock serializes concurrent applies, so bot and site starting together is safe.

## Deploy procedure

1. Build and publish images: `.\publish-docker.ps1 -Push` (or `-RemoteHost <host>`).
2. If credentials changed: `.\deploy-secrets-remote.ps1 -RemoteHost <host> -RemoteUser <user>`.
3. On the host: `docker-compose pull && docker-compose up -d`.
4. Verify: `curl http://localhost:5013/health`, `docker ps` health columns, `docker logs` for `bot` and `site`.

Rollback is the same procedure with the previous image tag.

## Health

| Check | Detail |
|---|---|
| `GET /health` | `HealthController`, anonymous, returns 200 with `{ status, timestamp }`. Used by the compose healthcheck (`curl -f`, 10s interval, 3 retries, 40s start period). |
| `GET /home/alive` | Anonymous DB connectivity probe (runs a query). |
| `GET /home/alivediscord` | 200 when the site's Discord gateway connection is up, 503 otherwise. |
| RabbitMQ | Compose healthcheck `rabbitmq-diagnostics -q ping`; management UI on `localhost:15672` on the host (SSH tunnel from outside). |

## Monitoring

- Bugsnag captures unhandled exceptions in Release builds.
- Site exposes prometheus-net `/metrics`, restricted to the global `Admin` role.
- Scheduled jobs log to the `AutomationLogs` table (rows older than 7 days are purged automatically).

## Legacy

`deploy/systemd/` contains stale blue-green LXC units from the removed deployment model. Pending removal; do not use.
