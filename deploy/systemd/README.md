# EGG9000 Bot blue/green systemd setup (LXC)

## 1) Install prerequisites in the container

- `dotnet-runtime-9.0`
- `rsync`

## 2) Create runtime user and folders

```bash
useradd --system --home /opt/egg9000 --shell /usr/sbin/nologin egg9000 || true
mkdir -p /opt/egg9000/blue /opt/egg9000/green /opt/egg9000/incoming /etc/egg9000
chown -R egg9000:egg9000 /opt/egg9000
chmod 755 /opt/egg9000 /opt/egg9000/blue /opt/egg9000/green /opt/egg9000/incoming
```

## 3) Add shared environment values

Create `/etc/egg9000/bot.common.env`:

```env
ConnectionStrings__DefaultConnection=...
ConnectionStrings__ClientId=...
ConnectionStrings__Token=...
ConnectionStrings__ClientSecret=...
ConnectionStrings__BugSnagApiKey=...
ConnectionStrings__APILinkURL=...
ConnectionStrings__RabbitMQServer=...
```

## 4) Install units and deploy script

Copy files from this folder to container:

- `egg9000-blue.service` -> `/etc/systemd/system/egg9000-blue.service`
- `egg9000-green.service` -> `/etc/systemd/system/egg9000-green.service`
- `egg9000-bot-deploy.sh` -> `/usr/local/bin/egg9000-bot-deploy.sh`

Then:

```bash
chmod +x /usr/local/bin/egg9000-bot-deploy.sh
systemctl daemon-reload
systemctl enable egg9000-blue.service egg9000-green.service
```

## 5) First deployment

1. Publish locally:
   - `dotnet publish EGG9000.Bot/EGG9000.Bot.csproj -c Release -o ./publish-bot`
2. Copy to container incoming folder (`rsync`/`scp`) as `/opt/egg9000/incoming/`
3. Run deploy script on container:
   - `sudo /usr/local/bin/egg9000-bot-deploy.sh /opt/egg9000/incoming`

Script behavior:

- Detects active color.
- Copies incoming build to inactive color folder.
- Restarts inactive service and waits for healthy active state.
- Stops old active service after successful cutover.

## 6) Windows one-command deployment

Use `deploy/systemd/deploy-to-lxc.bat` from Windows to publish, copy, and trigger deploy.

Defaults:

- Host: `192.168.0.190`
- User: `root`

Usage:

```bat
deploy\systemd\deploy-to-lxc.bat
deploy\systemd\deploy-to-lxc.bat 192.168.0.190 root
```

Requirements on Windows:

- `dotnet` CLI in `PATH`
- OpenSSH client (`ssh`, `scp`) in `PATH`
- SSH key or credential access to the container

## 7) Useful checks

```bash
systemctl status egg9000-blue.service
systemctl status egg9000-green.service
journalctl -u egg9000-blue.service -n 200 --no-pager
journalctl -u egg9000-green.service -n 200 --no-pager
