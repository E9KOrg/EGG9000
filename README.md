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

The bot and site run under the `DEV9002` build configuration, using User Secrets ID `DEV9001`. Create `secrets.json` at the path for your platform:

**Windows:** `%APPDATA%\Microsoft\UserSecrets\DEV9001\secrets.json`

<br>

**Linux / WSL2:** `docker-compose.dev.yml` mounts `${APPDATA}/Microsoft/UserSecrets` into containers, but `dotnet run` reads from `~/.microsoft/usersecrets/`. Set `APPDATA` once so both point to the same place, then create the file and symlink:
```bash
# Add to ~/.bashrc or ~/.zshrc
export APPDATA="$HOME/.local/share"

mkdir -p ~/.local/share/Microsoft/UserSecrets/DEV9001
mkdir -p ~/.microsoft/usersecrets
ln -s ~/.local/share/Microsoft/UserSecrets/DEV9001 ~/.microsoft/usersecrets/DEV9001
```
Secrets file: `~/.local/share/Microsoft/UserSecrets/DEV9001/secrets.json`

<br>

All platforms — populate with:
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

**5. About `docker-compose.dev.yml`**

`docker-compose.dev.yml` defines three services: `bot`, `site`, and `rabbitmq`. For local development you run the bot via `dotnet run` (step 8) so you don't need the bot container. The site and rabbitmq services are started later (step 10). Running the full stack is only needed if you want to test the dockerized bot image:

```
docker-compose -f docker-compose.dev.yml up
```

<br>

**6. Initialize the database**

Migrations apply automatically when the bot first starts. To apply them ahead of time, install the EF Core CLI tools and run:

```
dotnet tool install --global dotnet-ef
dotnet ef database update --project EGG9000.Common --startup-project EGG9000.Bot --configuration DEV9002
```

<br>

**7. Seed the Guilds table**

The bot needs a row in `Guilds` for your test Discord server before it will function. Replace `<your-server-id>` with your Discord server's ID (visible by right-clicking the server icon with Developer Mode on):

```
docker exec -it egg9000-pg psql -U <username> -d <dbname> -c "INSERT INTO \"Guilds\" (\"Id\", \"Name\", \"DiscordSeverId\", \"OverflowServersJson\", \"_coopSettingsJson\", \"_eventCustomizationsJson\", \"_faqTopicsJson\", \"_channelDetailsJson\", \"AddOutsideCoops\") VALUES (<your-server-id>, 'Dev Server', <your-server-id>, '[]', '[]', '[]', '[]', '[]', true);"
```

<br>

**8. Run the bot**

```
cd EGG9000.Bot
dotnet run --configuration DEV9002
```

The bot will connect to your test Discord server. If migrations weren't applied in step 6, they run now.

<br>

**9. Register via the bot**

In your test Discord server, run the `/register` slash command with your Egg Inc. ID. This creates your `DBUser` row and is required before logging into the site.

<br>

**10. Start the site**

Make sure the postgres container from step 4 is still running (`docker start egg9000-pg` if it stopped). Then:

```
docker-compose -f docker-compose.dev.yml up site
```

Docker Compose automatically starts `rabbitmq` first (the site depends on it). The site is exposed at `http://localhost:5013`.

<br>

**11. Log in to the site**

Go to `http://localhost:5013` and log in with Discord. This creates your ASP.NET Identity rows (`AspNetUsers`, `AspNetUserLogins`) which the admin SQL below depends on.

<br>

**12. Seed yourself as admin**

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

**13. Configure your server**

Navigate to `http://localhost:5013/Admin/ConfigureServer` to set up channels, roles, and other server settings.

<br>

**14. Seed contracts into the Contracts table**

Run the bot using `dotnet run --configuration DEV9002` and then go to /mycontractsettings and navigate to the collegtibles embed which will seed contracts and mark released custom eggs as such.

---

**Website (local)**

Requires the postgres container from step 4 to be running (`docker start egg9000-pg` if it stopped).
```
cd EGG9000.Site
dotnet watch --no-hot-reload
```
To bypass Discord login: `/Home/DebugLogin?id={yourdiscordid}` (requires at least one prior login to the dev DB).
