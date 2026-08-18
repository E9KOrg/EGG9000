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

**2. Create your own Discord bot and test server**

You do not need access to the shared E9K dev bot. Use your own, in your own server.

1. Create a Discord server to test in, if you do not already have one.
2. Go to <https://discord.com/developers/applications> and click **New Application**.
3. **Bot** tab → **Reset Token** → copy it. This is `ConnectionStrings:Token`.
4. Still on the **Bot** tab, enable all three **Privileged Gateway Intents** (Presence, Server Members, Message Content). The bot requests `GuildMembers` and `MessageContent`, and will fail to connect without them.
5. **OAuth2** tab → copy **Client ID** and **Client Secret**. These are `ConnectionStrings:ClientId` and `ConnectionStrings:ClientSecret`.
6. On the same tab, add these **Redirects**, which is where Discord sends you back after login:
   - `http://localhost:5013/signin-discord`
7. **OAuth2 → URL Generator**: tick scopes `bot` and `applications.commands`, give it **Administrator** under bot permissions for a test server, then open the generated URL and invite the bot to your server.

Step 5 records your server's id for you, so the bot and site both use it instead of the shared dev server.

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

> Note the username, password, and database name you used. Step 5 writes a `DefaultConnection` key into `secrets.json` for you, and these are the values that go in it.

<br>

**5. Run setup**

One command does the rest of the setup. It creates your `secrets.json`, tells you where it is and which keys to fill in, applies migrations, seeds the `Guilds` row for your test Discord server, records your server id, creates the `Admin` role, and grants it to you.

You do not create the secrets file yourself and you do not need to know which keys go in it. Expect to run this twice:

1. First run: it creates `secrets.json`, prints its full path, and lists each missing key with what it is for.
2. Open that file, paste in the values from step 2 and the database details from step 4.
3. Run it again. It picks up from where it stopped.

```
cd EGG9000.Onboarding
dotnet run --configuration DEV9002
```

It prints what it did at each step and is safe to run again at any time. A second run reports anything already in place as `ok` and changes nothing.

It also lists the Discord servers your bot is in and lets you pick one, so you do not need to copy a server ID by hand.

**It will pause and wait.** Granting yourself admin needs a website login, which only exists after you sign in through Discord in a browser. When setup reaches that point it prints instructions and waits, polling until you have logged in. Leave it running and carry on with steps 6 to 8 in another terminal. It picks up on its own once your login appears.

Optional flags, mostly for scripted runs:

| Flag | Effect |
|------|--------|
| `--guild <id>` | Skip the server picker |
| `--admin <id>` | Skip the Discord user ID prompt |
| `--no-wait` | Never wait for a website login, report that step as skipped instead |

> Setup refuses to run under the `DEV9001` and `Release` configurations, which point at the production database. There is no override.

> The file it writes is the one the `DEV9002` configuration reads (user secrets id `DEV9001`). The bot and the site read the same file when passed the same flag, which is why step 6 passes it to both.

<br>

**6. Start the application**

Two options, pick one:

---

**Option A - dotnet (recommended for active development)**

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
dotnet watch --no-hot-reload --configuration DEV9002
```

> `--configuration DEV9002` is required, not optional. Without it the site builds `Debug`, which reads a different secrets file from the one step 5 filled in, so Discord login will not be configured and step 8 cannot work.

Site at `http://localhost:5013`. To bypass Discord login: `/Home/DebugLogin?id={yourdiscordid}` (requires at least one prior login to the dev DB).

---

**Option B - docker-compose (full stack)**

Runs bot, site, and rabbitmq as a Docker stack. Use this to test the dockerized bot image or replicate the production environment.

> **Different secrets file:** the Docker images use user secrets id `dotnetcore-coopcodes-f186fb4c-b5ba-4267-9a58-9d24c71afb0a`, not the one step 5 wrote. Copy your finished `secrets.json` to that id's folder, alongside the one setup created.
>
> **Linux:** `docker-compose.dev.yml` mounts `${APPDATA}/Microsoft/UserSecrets` into the containers. If your secrets live under `~/.local/share`, point `APPDATA` at it so the mount resolves:
> ```bash
> # Add to ~/.bashrc or ~/.zshrc
> export APPDATA="$HOME/.local/share"
> ```
>
> **Connection string change required:** Both containers read `secrets.json` but can't reach `localhost` from inside Docker. Change `Host=localhost` to `Host=host.docker.internal` in your `secrets.json` before starting the stack. Revert to `localhost` when switching back to Option A.
>
> **Linux:** `host.docker.internal` is not available by default - add `extra_hosts: ["host.docker.internal:host-gateway"]` under both `bot` and `site` in `docker-compose.dev.yml`, or point `Host=` at your machine's LAN IP.

```
docker-compose -f docker-compose.dev.yml up
```

Site at `http://localhost:5013`.

---

<br>

**7. Register via the bot**

In your test Discord server, run the `/register` slash command with your Egg Inc. ID. This creates your `DBUser` row and is required before logging into the site.

<br>

**8. Log in to the site**

Go to `http://localhost:5013` and log in with Discord. This creates your ASP.NET Identity rows, which is what the waiting setup command from step 5 is looking for. Once it sees them it grants you the `Admin` role and finishes.

If you closed setup before logging in, run it again now:
```
cd EGG9000.Onboarding
dotnet run --configuration DEV9002
```

<br>

**9. Configure your server**

Navigate to `http://localhost:5013/Admin/ConfigureServer` to set up channels, roles, and other server settings.

<br>


For any issues with setup or running the bot, please reach out in the Dev server or ping @daveed or @kendrome.
