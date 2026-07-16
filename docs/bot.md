# EGG9000.Bot

Discord slash-command bot built on Discord.NET. Manages Egg Inc co-op contracts across Discord guilds: registration, co-op assignment, status tracking, leaderboards, merits/demerits, and notifications.

Entry point: `EGG9000.Bot/Program.cs`.

## Startup

`Program.cs` builds a generic host with NLog, then delegates all DI wiring to `BotHostFactory.ConfigureServices` (extracted from `Program.cs` so the integration-test suite can verify the container).

- In Release builds only, pending EF Core migrations are applied at startup (`Database.MigrateAsync()`) before the host runs. Dev configs (`Debug`, `DEV9001`, `DEV9002`) stay manual.
- `BOT_COLOR` (optional) suffixes the machine name used in log output.
- Configuration and secrets resolve through `SecretsHelper`: Docker secrets first, then user secrets or environment variables (`ConnectionStrings__*`).

### DI summary (`BotHostFactory.cs`)

| Area | Registrations |
|---|---|
| Data | `IDbContextFactory<ApplicationDbContext>` (Npgsql, `QueryCountingInterceptor`), `DatabaseCache`, `CoopStatsCache`, `CoopAssignmentLookup`, cache refresh hosted services |
| Discord | `DiscordHostedService`, `DiscordSocketClient`, `InteractionService` (RunMode.Sync, compiled lambdas), `InteractionRoutingService`, `DiscordQueueService` (`IDiscordQueue`) |
| Commands support | `JobService`, `CoopsBeingCreatedService`, `MessageHandlerService`, `DiscordUserService` |
| Automated | All `_UpdaterBase<T>` jobs as hosted services (see Automated jobs) |
| Messaging | MassTransit consumers; RabbitMQ in Release, in-memory bus if no RabbitMQ connection string, `PublishEndpointMock` in Debug |
| Errors | Bugsnag: real client in Release, suppressed no-op client in Debug |

`UpdaterOptions<T>` overrides per-job delayed start: `LeaderboardUpdater` 15 min, `ThreadsCoopStatusUpdater` 5 min.

## Command pattern

Commands are Discord.NET `InteractionService` modules.

- Modules derive `Interactions/E9KModuleBase.cs`, which derives `InteractionModuleBase<SocketInteractionContext>`. `BeforeExecuteAsync` opens a fresh `ApplicationDbContext` as `Db` per command; `AfterExecuteAsync` disposes it.
- Dependencies are constructor-injected via primary constructors, resolved per invocation.
- Standard Discord.NET attributes apply: `[SlashCommand]`, `[Group]`, `[ComponentInteraction]`, `[ModalInteraction]`, `[UserCommand]`, `[Autocomplete]`, `[Summary]`, `[DefaultMemberPermissions]`, `[CommandContextType]`.

### Access gates

`Interactions/StaffGate.cs` defines `StaffTier` and `[StaffOnly(tier)]`, a precondition usable on modules and methods. Unlike `[DefaultMemberPermissions]` (top-level slash commands only), it also protects component and modal handlers.

| Tier | Discord permission equivalent |
|---|---|
| ChickenTender | ModerateMembers |
| FarmHand | CreatePrivateThreads |
| CluckingCoordinator | ManageChannels |
| Admin | Administrator |

Tables below use these tier names. Commands without `[StaffOnly]` are gated by the equivalent `[DefaultMemberPermissions]`.

`Interactions/BuildConfigGate.cs` defines `[BuildConfigOnly(configs)]`: `InteractionRoutingService` removes disallowed modules before registration, so the command never appears in Discord outside the allowed build configs.

### Registration and routing (`Services/InteractionRoutingService.cs`)

- Discovers all modules in the assembly, removes `[BuildConfigOnly]` modules disallowed for the current config, registers everything globally (`RegisterCommandsGloballyAsync`). No guild-scoped registration.
- `PurgeStaleGuildCommandsAsync` deletes leftover guild-scoped commands from older deploys (capped at 90 per guild as a safety check; permission overrides are logged before delete).
- Executes interactions off a 50-slot semaphore; over capacity responds with an ephemeral overload notice.
- Records Prometheus metrics (`bot_interaction_duration_seconds`, `bot_interaction_total`, `bot_interaction_failures_total`) and `RuntimeMetrics` counters. Autocomplete is excluded from command counters.
- `*Executed` result events render error components (exception frame or error message) back to the user.

## Command reference

### Public commands

| Command | File | DM | Description |
|---|---|---|---|
| `/ping` | Ping.cs | yes | Liveness check |
| `/register` | RegisterCommandsSlash.cs | no | Register your Egg Inc account |
| `/updateid` | RegisterCommandsSlash.cs | yes | Update your Egg Inc ID |
| `/moveserver` | RegisterCommandsSlash.cs | no | Move registration to a different guild |
| `/accept` | RegisterCommandsSlash.cs | no | Accept server rules |
| `/userstatus` | UserStatusCommands.cs | yes | Your registration/co-op status |
| `/fixfullcooperror` | ContractCommandsSlash.cs | no | Self-service fix for the "co-op full" error |
| `/createcoop` | ContractCommandsSlash.cs | no | Create a co-op for a contract |
| `/mycontractsettings` | ContractSettings.cs | yes | Manage your contract settings |
| `/coopsettings` | CoopSettings.cs | no | Co-op notification preferences |
| `/showeb` / `/hideeb` | CoopSettings.cs | no | Add/remove EB in your server nickname |
| `/merits` | MeritCommands.cs | yes | List your merits |
| `/demerits` | DemeritCommands.cs | yes | List your demerits |
| `/faq` | FAQCommandSlash.cs | yes | Topic explanations |
| `/starttestprocess` | MiscCommandsSlash.cs | yes | Egg Inc ID screenshot OCR flow |
| `/trackeb` | MiscCommandsSlash.cs | yes | EB change since last run |
| `/nextrank` | MiscCommandsSlash.cs | yes | SE/PE needed for next rank |
| `/callstaff` | MiscCommandsSlash.cs | no | Request staff help |
| `/apod` | NasaCommands.cs | no | NASA Astronomy Picture of the Day |
| `/viewinventory` | Informational/ArtifactCommands.cs | yes | Your artifact inventory |
| `/savedafsets` | Informational/ArtifactCommands.cs | no | Your saved artifact sets |
| `/chasing` | Informational/ChasingCommand.cs | yes | Players ahead of and behind you |
| `/craft` / `/craftedcount` | Informational/CraftCommand.cs | yes | Crafting requirements / craft counts |
| `/ebhistory` | Informational/EBHistoryCommand.cs | no | Key points in your EB history |
| `/formulae mer` / `llc` / `eb` | Informational/FormulaCommands.cs | yes | Game formula calculators |

### Staff commands (flat)

| Command | File | Access | Description |
|---|---|---|---|
| `/makepublic` | ContractCommandsSlash.cs | CluckingCoordinator | Make a co-op public |
| `/movegrade` | ContractCommandsSlash.cs | FarmHand | Move a user to a different grade co-op |
| `/findcoopforuser` | ContractCommandsSlash.cs | FarmHand | Find and assign a co-op for a user |
| `/addcoop` | ContractCommandsSlash.cs | FarmHand | Track an outside co-op |
| `/fixreference` | ContractCommandsSlash.cs | FarmHand | Silent move plus reference fix |
| `/movetocoop` | ContractCommandsSlash.cs | FarmHand | Move a user to a specific co-op |
| `/removefromcoop` | ContractCommandsSlash.cs | FarmHand | Remove a user the bot sees as unjoined |
| `/leavecoop` | ContractCommandsSlash.cs | FarmHand | Remove a user from a co-op (glitch fix) |
| `/makeprivate` | ContractCommandsSlash.cs | Admin | Make a co-op private |
| `/deletecontract` | ContractCommandsSlash.cs | Admin | Delete a contract channel |
| `/disable` / `/enable` | StaffCommands.cs | FarmHand / CluckingCoordinator | Block or restore co-op assignment for a user |
| `/clearcustomeggs` | StaffCommands.cs | Admin | Clear all custom eggs and emoji |
| `/as` | StaffCommands.cs | Admin | Send a message as the bot |
| `/newcoopcode` | NewCode.cs | CluckingCoordinator | Generate a co-op code plus channel |
| `/deletecoop` | NewCode.cs | Admin | Delete a co-op from Discord and DB |
| `/updatechannel` | MiscCommandsSlash.cs | CluckingCoordinator | Force a co-op/contract channel update |
| `/kick` | BanCommands.cs | Admin | Kick user(s) with DM, optional account ban |

`/kick` is intentionally flat to preserve its pre-migration name.

### `/a` group (AdminModule, FarmHand)

Partial class `AdminModule` (`[Group("a")]`), extended across several files.

| Subcommand | File | Description |
|---|---|---|
| `fixfullcooperror` | ContractCommandsSlash.cs | Fix the "co-op full" error for another user |
| `contractsettings` | ContractSettings.cs | Manage another user's contract settings |
| `faq` | FAQCommandSlash.cs | FAQ with staff response options |
| `addmerit` / `removemerit` / `meritsforuser` | MeritCommands.cs | Merit management |
| `register` / `updateid` / `removeid` / `accept` | RegisterCommandsSlash.cs | Registration management for another user |
| `clean` | RegisterCommandsSlash.cs | Remove unpinned messages from the channel |
| `tempcustomcoopname` | MiscCommandsSlash.cs | Temporary custom co-op name |
| `renamecoop` | MiscCommandsSlash.cs | Rename a co-op channel |
| `temprole` | MiscCommandsSlash.cs | Timed temporary role for users |
| `selectroleusers` | StaffCommands.cs | Pick N random users with a role |
| `pingeveryoneincoop` | StaffCommands.cs | Ping all co-op members with a message |
| `fixjoinissue` | StaffCommands.cs | Fix join visibility issues |
| `viewinventory` | Informational/ArtifactCommands.cs | Another user's artifact inventory |
| `userstatus` | UserStatusCommands.cs | Another user's status |

### `/admin` group (AdminGroupModule, Admin)

| Subcommand | File | Description |
|---|---|---|
| `configure` | ConfigureCommands.cs | In-Discord server config, mirrors the website |
| `rankup` | RankupCommands.cs | Customize rank-up announcements |
| `editfaq` | EditFaqCommands.cs | Edit this server's FAQ topics |
| `adddemerit` / `removedemerit` / `demeritsforuser` / `nodemerit` | DemeritCommands.cs | Demerit management |

### `/bot` group (BotGroupModule, FarmHand)

| Subcommand | File | Description |
|---|---|---|
| `botstatus` | AdminStatusModule.cs | One-look bot/DB/deploy/load status |
| `sysload` | AdminStatusModule.cs | Runtime, Discord, DB, and process load |
| `status` | StaffCommands.cs | Bot status |
| `dbload` | StaffCommands.cs | DB load, cache sizes, process memory |
| `coopstats` | StaffCommands.cs | Active co-op stats |
| `restartservice` / `stopservice` / `runservice` / `startservice` | StaffCommands.cs | Runtime control of automated services |
| `setversions` (Admin) | ApiVersionCommands.cs | Update the Egg Inc API version triple; validated against the live API, then broadcast to all instances over the bus |

### `/b` group (BanCommands.cs, CluckingCoordinator)

| Subcommand | Description |
|---|---|
| `banlist` | List users/EIDs banned via `/kick` |
| `removeban` | Remove a ban and its EIDs |

### `/test` group (TestSuiteCommands.cs, dev builds only)

Registered only in `Debug`/`DEV9001`/`DEV9002` via `[BuildConfigOnly]`. Subcommands: `seedcoops`, `clearseed`, `refreshstats`, `runembed`, `loadmetrics`, `assignme`. Seeds fake co-ops, forces stats refreshes, and injects fake metric load for testing.

### Context menu commands (ContextUserCommands.cs)

`Userstatus`, `Contract Settings`, `Rockets Tracker` - right-click user commands, FarmHand-gated.

### Component-only module

`ShipReturnDMSettings.cs` has no slash command: it holds the button/menu/modal handlers for ship-return DM settings, reached from other embeds.

## Message handlers (`Services/MessageHandlerService.cs`)

Subscribes to gateway `MessageReceived`:

- Awards a merit and posts a thank-you when a member boosts the configured guild.
- Screenshot registration: OCR on posted Egg Inc screenshots in guild channels.
- OCR test flow for `/starttestprocess` replies in DMs.
- Ping-on-message: DMs co-op members who opted in when someone posts in their co-op channel/thread.
- Typed-command warning: replies with a clickable command link when a user sends a slash command as plain text.

## Gateway events (`Services/DiscordUserService.cs`)

- `UserJoined`: roles, welcome, co-op channel access, overflow handling, disabled/banned warnings.
- `UserLeft`: resets registration flags, records last guild.
- `ChannelDestroyed` / `ThreadDeleted`: marks co-op/contract channels and threads deleted in DB.

## Automated jobs (`Automated/`)

All extend `_UpdaterBase<T>`: fixed interval or Cronos cron (Central Standard Time), optional delayed start (overridable via `UpdaterOptions<T>`), non-overlapping runs, per-run `AutomationLogs` rows, `ChangeUpdateInterval` for self-tuning, and a watchdog that DMs the bot owner when a job stops completing. Discord writes from jobs go through `IDiscordQueue` (`_queue` on the base class) so background traffic cannot starve interaction responses.

| Job | Schedule (Release) | Delay | Purpose |
|---|---|---|---|
| `ArtifactCheaters` | 30 min (Debug 1 min) | none | Artifact fairness z-scores, crafting XP outliers, warnings |
| `CleanApiKeyRequestLogs` | 12 h | 5 min | Deletes API key request logs >7d, daily usage rows >90d |
| `CleanAutomationLogs` | 12 h | 5 min | Deletes `AutomationLogs` rows >7d |
| `ContractUpdater` | 90 min | 10 min | Contract channel messages, detects co-ops from backups |
| `CoopStatsRefreshService` | 3 min | 1 min | Refreshes `CoopStatsCache`, maintains opt-in stats embeds |
| `CreateCoopThreads` | 1 min | none | Creates threads for co-ops awaiting assignment |
| `CreateCoopViaAPI` | 1 min | none | Creates co-ops through the Egg Inc API |
| `EventUpdater` | 1 min | none | Fetches game events, posts notifications |
| `HandleGradeChanges` | cron `30 10,18,23 * * 1,3,5` | - | Contract grade promotions |
| `LeaderboardUpdater` | 60 min | 15 min | Leaderboards, break violations, EB role sync |
| `ManageOverflow` | 5.6 min | none | Overflow server roles and channel permissions |
| `NewContracts` | 1 min, self-tunes 15 s to 10 min | none | Detects new contracts, creates guild contract records, custom eggs |
| `RefreshNasaApod` | 15 min | none | Caches NASA APOD, posts to configured channels |
| `RemoveTempRoles` | 5 min | none | Removes expired temporary roles |
| `ShipReturnDM` | 15 s, self-tunes up to 1 min | none | DMs users when ships return |
| `StaffCoopsMessage` | 30 min | none | Staff co-op activity summary |
| `ThreadsCoopStatusUpdater` | 15 min (Debug 20 min) | 5 min | Polls the Egg Inc API, updates co-op thread status embeds |
| `UpdateBackups` | 1 min | none | Fetches up to 75 fresh plus 5 stale user backups per run |
| `UserCXPUpdater` | cron `0 9 * * MON,WED,FRI` | - | CXP scores for all accounts, history tracking |
| `UserGrades` | 4 h | 30 min | Refreshes user grades, 8 parallel API calls |
| `UserSnapShots` | 5 h | 1 min | Daily user snapshots for leaderboard history |

Outside `_UpdaterBase`:

| Service | Schedule | Purpose |
|---|---|---|
| `BotMetricsPublisher` | 15 s (`PeriodicBackgroundService`) | Publishes `BotMetricsSnapshotMessage` over the bus; the site exposes it as `bot_*` gauges on `/metrics` |
| `RankupMessageSeeder` | one-shot at startup | Seeds default rank-up messages for the palace guild, idempotent |

## Cron jobs (`Jobs/` via `Services/JobService.cs`)

`JobService` reflection-discovers `[Job("cron")]` methods (Cronos, seconds field, Central Standard Time), runs them on schedule, skips still-running jobs, and exposes run/stop/start used by the `/bot` service commands.

| Method | Cron | Purpose |
|---|---|---|
| `UserDMsJob.WarningBreakExpiring` | `0 */30 * * * *` | DMs users whose break expires within a day, once per break |
| `UptimeKuma.Send` | `0/15 * * * * *` | Release-only heartbeat to an external uptime monitor; fails fast, no retries |

## Messaging (MassTransit)

| Direction | Message | Handler / publisher |
|---|---|---|
| Consume | shutdown | `ShutdownConsumer` |
| Consume | restart | `RestartConsumer` |
| Consume | cache expiry | `ExpireCacheConsumer` |
| Consume | `UpdateApiVersionsMessage` | `UpdateApiVersionsConsumer`, per-instance temporary queue so version updates fan out to every running process |
| Publish | `UpdateApiVersionsMessage` | `/bot setversions` |
| Publish | `BotMetricsSnapshotMessage` | `BotMetricsPublisher`, every 15 s |

Transport: RabbitMQ in Release; in-memory MassTransit bus when Release has no RabbitMQ connection string; `PublishEndpointMock` in Debug.
