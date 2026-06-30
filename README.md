# EGG9000


If you run into any issues with the setup, please ask around in the Dev server or ping @daveed or @kendrome.

### Dev Setup

**Prerequisites**

**Windows:** Visual Studio with the **ASP.NET and web development** and **.NET desktop development** workloads (VS Code or Rider work too), .NET 10 SDK, Docker Desktop.

**WSL2:** Install .NET 10 SDK and Docker Desktop (on Windows, not inside WSL2). Enable WSL2 integration in Docker Desktop under Settings → Resources → WSL Integration.
```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt update && sudo apt install -y dotnet-sdk-10.0
```

**Linux:** Install .NET 10 SDK and Docker Engine.
```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb
sudo apt update && sudo apt install -y dotnet-sdk-10.0
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER && newgrp docker
```

---

**1. Clone the repo**
```
git clone https://github.com/E9KOrg/EGG9000.git
cd EGG9000
```

<br>

**2. Set up secrets**

Two User Secrets IDs are in use — one for `dotnet run` (Option A in step 7), one for the Docker images (Option B):

- **Option A (dotnet):** `DEV9001`
- **Option B (Docker):** `dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a`

Create `secrets.json` at the path for your platform and option. If you plan to use both, create the file at both paths.

**Windows** — create `secrets.json` at:
- Option A: `%APPDATA%\Microsoft\UserSecrets\DEV9001\secrets.json`
- Option B: `%APPDATA%\Microsoft\UserSecrets\dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a\secrets.json`

**Linux / WSL2** — the base path varies by distro. Run this from the repo root to have .NET create the file and find where it looks for it:
```bash
cd EGG9000.Bot && dotnet user-secrets set "ConnectionStrings:DefaultConnection" "placeholder"
```
Known locations (check which one your distro uses):
- `~/.microsoft/usersecrets/<id>/secrets.json`
- `~/.local/share/Microsoft/UserSecrets/<id>/secrets.json`

Replace `<id>` with `DEV9001` for Option A or the Docker ID above for Option B.

`docker-compose.dev.yml` mounts `${APPDATA}/Microsoft/UserSecrets` into containers. If your secrets live under `~/.local/share`, set `APPDATA` so the compose mount can find them:
```bash
# Add to ~/.bashrc or ~/.zshrc
export APPDATA="$HOME/.local/share"
```

<br>

All platforms — populate `secrets.json` with:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;PORT=5433;Database=<dbname>;Username=<username>;Password=<password>;SSL Mode=Disable",
    "Token": "<discord bot token>",
    "ClientId": "<discord application client id>",
    "ClientSecret": "<discord application client secret>",
    "BugSnagApiKey": "<bugsnag key — leave blank for dev>",
    "ApiSalt": "<egg inc api salt>",
    "RabbitMQServer": "rabbitmq|e9k|devpassword",
    "CPGuildId": "<your test discord server id>"
  }
}
```

> **ApiSalt** is required for authenticated Egg Inc. API endpoints (`ei_ctx/get_contracts_info`, `ei_ctx/get_contract_player_info`). Without it those calls are silently disabled but everything else still works.

<br>

**3. Restore NuGet packages**

```
dotnet restore
```

Restores packages at the versions pinned in the project files without upgrading.

<br>

**4. Start PostgreSQL**

**Windows:**
```
docker run -d --name egg9000-pg ^
  -e POSTGRES_USER=<username> ^
  -e POSTGRES_PASSWORD=<password> ^
  -e POSTGRES_DB=<dbname> ^
  -p 5433:5432 ^
  postgres:latest
```

**Linux / WSL2:**
```bash
docker run -d --name egg9000-pg \
  -e POSTGRES_USER=<username> \
  -e POSTGRES_PASSWORD=<password> \
  -e POSTGRES_DB=<dbname> \
  -p 5433:5432 \
  postgres:latest
```

> Make sure the connection string in `secrets.json` matches the username, password, and database name you used here.

<br>

**5. Initialize the database**

To initialize the database or to run migrations. Install the EF Core CLI tools and run:

```
dotnet tool install --global dotnet-ef
dotnet ef database update --project EGG9000.Common --startup-project EGG9000.Bot --configuration DEV9002
```

<br>

**6. Seed the Guilds table**

The bot needs a row in `Guilds` for your test Discord server before it will function. Replace `<your-server-id>` with your Discord server's ID (visible by right-clicking the server icon with Developer Mode on):

```
docker exec -it egg9000-pg psql -U <username> -d <dbname> -c "INSERT INTO \"Guilds\" (\"Id\", \"Name\", \"DiscordSeverId\", \"OverflowServersJson\", \"_coopSettingsJson\", \"_eventCustomizationsJson\", \"_faqTopicsJson\", \"_channelDetailsJson\", \"AddOutsideCoops\", \"MinimumRunningScore\", \"FAQTopicsEnabled\", \"FAQTopicCooldownMinutes\", \"DisableBG\", \"AllowGuilds\", \"PublicScoreGrid\", \"RemoveFindCoopSpot\") VALUES (<your-server-id>, 'Dev Server', <your-server-id>, '[]', '[]', '[]', '[]', '[]', true, 0, false, 0, false, false, false, false);"
```

<br>

**7. Start the application**

Two options — pick one:

---

**Option A — dotnet (recommended for active development)**

Runs bot and site directly. Requires only the postgres container from step 4.

**Run the bot:**
```
cd EGG9000.Bot
dotnet run --configuration DEV9002
```

The bot connects to your test Discord server. Confirm you can see the bot online on discord and it responds to `/ping` or `/a ping`

**Run the site** (new terminal):
```
cd EGG9000.Site
dotnet watch --no-hot-reload
```

Site at `http://localhost:5013`. To bypass Discord login: `/Home/DebugLogin?id={yourdiscordid}` (requires at least one prior login to the dev DB).

---

**Option B — docker-compose (full stack)**

Runs bot, site, and rabbitmq as a Docker stack. Use this to test the dockerized bot image or replicate the production environment.

> **Connection string change required:** Both containers read `secrets.json` but can't reach `localhost` from inside Docker. Change `Host=localhost` to `Host=host.docker.internal` in your `secrets.json` before starting the stack. Revert to `localhost` when switching back to Option A.
>
> **Linux:** `host.docker.internal` is not available by default — add `extra_hosts: ["host.docker.internal:host-gateway"]` under both `bot` and `site` in `docker-compose.dev.yml`, or point `Host=` at your machine's LAN IP.

```
docker-compose -f docker-compose.dev.yml up
```

Site at `http://localhost:5013`.

---

<br>

**8. Register via the bot**

In your test Discord server, run the `/register` slash command with your Egg Inc. ID. This creates your `DBUser` row and is required before logging into the site.

<br>

**9. Log in to the site**

Go to `http://localhost:5013` and log in with Discord. This creates your ASP.NET Identity rows (`AspNetUsers`, `AspNetUserLogins`) which the admin SQL below depends on.

<br>

**10. Seed yourself as admin**

Connect to the postgres container:

```
docker exec -it egg9000-pg psql -U <username> -d <dbname>
```

Then paste:

```sql
-- 1. Create the Admin role (skip if already exists)
INSERT INTO "AspNetRoles" ("Id", "Name", "NormalizedName", "ConcurrencyStamp")
VALUES (gen_random_uuid()::text, 'Admin', 'ADMIN', gen_random_uuid()::text)
ON CONFLICT DO NOTHING;

-- 2. Add yourself (replace <your-discord-id> with your numeric Discord user ID)
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT u."Id", r."Id"
FROM "AspNetUsers" u, "AspNetRoles" r
WHERE r."NormalizedName" = 'ADMIN'
  AND u."Id" = (
    SELECT "UserId" FROM "AspNetUserLogins"
    WHERE "ProviderKey" = '<your-discord-id>'
  )
ON CONFLICT DO NOTHING;
```

<br>

**11. Configure your server**

Navigate to `http://localhost:5013/Admin/ConfigureServer` to set up channels, roles, and other server settings.

<br>


For any issues with setup or running the bot, please reach out in the Dev server or ping @daveed or @kendrome.