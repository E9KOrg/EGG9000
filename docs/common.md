# EGG9000.Common

Shared class library consumed by `EGG9000.Bot` and `EGG9000.Site`. Contains the EF Core database layer, entity models, the Egg Inc API client, the co-op assignment engine, Discord service hosting, MassTransit consumers, and game-logic helpers.

## Overview

- Target: net10.0, `LangVersion: preview`.
- Key packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Discord.Net`, `Google.Protobuf` + `Grpc.Tools` (codegen only, transport is plain HTTP), `MessagePack`, `MassTransit`, `Newtonsoft.Json`, `SixLabors.ImageSharp(.Drawing)`, `Polly`, `NLog`, `Bugsnag`, `Humanizer`, `CsvHelper`.

## Layout

```
EGG9000.Common/
  Consumers/          MassTransit consumers + message types
  Contracts/          Assignment orchestration + rule engine (Assignment/)
  Coops/              ArtifactCombos (best artifact loadout search)
  Database/
    ApplicationDbContext.cs, CustomBackup.cs
    DatabaseCache.cs, CoopStatsCache.cs, CoopAssignmentLookup.cs
    QueryCountingInterceptor.cs, UtcDateTimeOffsetCommandInterceptor.cs
    Entities/         32 entity files
  EggIncAPI/          Egg Inc API client (partial class + secrets + sanitizer)
  Extensions/         Enum, IEnumerable, UInt32 extensions
  Factories/          StaticLoggerFactory, TimingsFactory
  Helpers/            Game logic, Discord, formatting, imaging (AfxSets/, ArtifactImaging/, Discord/)
  JsonData/           EmbeddedResource<T> loader + embedded game statics
  Migrations/         EF Core migrations (append-only)
  Mocks/              PublishEndpointMock
  Proto/              ei.proto, common.proto + reference captures
  Services/           Discord hosting, write queue, periodic base, metrics, BotLogger
```

## Database

### ApplicationDbContext (`Database/ApplicationDbContext.cs`)

Extends `IdentityDbContext<ApplicationUser>` and `IDataProtectionKeyContext`. Configured for PostgreSQL (Npgsql) with retry-on-failure and a 30s command timeout. `ApplicationDbContextFactory` in the same file is the design-time factory (resolves connection string from appsettings/user secrets, `MigrationsAssembly("EGG9000.Common")`).

31 DbSets (30 domain + `DataProtectionKeys`): `Guilds`, `Contracts` (`DBContract`), `Coops`, `DBUsers`, `UserCoopXrefs`, `UserCoopStatuses`, `GuildContracts`, `Demerit`, `Merit`, `Events` (`DBEvent`), `EventCustomizations`, `Donations`, `CustomEggs` (`DBCustomEgg`), `GlobalLeaderboardCoops`, `GlobalLeaderboardUsers`, `UserSnapShots`, `TemporaryRoles`, `ExpiringShells`, `AutomationLogs`, `UpcomingContracts`, `UserCsHistoryEntries`, `FAQTopics`, `RankupMessages`, `ResearchCostSubmissions`, `NasaApods`, `SeasonInfos`, `UserSeasonProgresses`, `ApiKeys`, `ApiKeyRequestLogs`, `ApiKeyDailyUsages`.

Behaviors wired into the context:

| Behavior | Mechanism |
|---|---|
| `ILastModified` auto-timestamps | `ChangeTracker.Tracked`/`StateChanged` set `LastModified = UtcNow` on add/modify |
| `AdminUserId` sentinel normalization | `NormalizeAdminUserIds()` in both `SaveChanges` overrides converts `Guid.Empty` to null on `Merit`/`Demerit` (Postgres enforces the FK; the old sentinel breaks inserts) |
| UTC `DateTimeOffset` writes | `UtcDateTimeOffsetConverter` on every mapped `DateTimeOffset` property, plus `UtcDateTimeOffsetCommandInterceptor` as a runtime net for raw query parameters |
| Test-seed exclusion | Global query filter drops coops with `CreatorID == Coop.TestSeedCreatorId`; DEV harness opts back in via `IgnoreQueryFilters()` |
| Frozen guild cache | `CachedGuilds` returns a `FrozenSet<Guild>` cached in `IMemoryCache` for 1 hour |
| Contract cache | `CachedEiContractsAsync()` merges the live contracts archive with DB rows into a `FrozenSet<Ei.Contract>` (1h TTL, 1min on degraded fetch); `ExpireCachedEiContracts()` evicts |
| Contract self-heal | `RegisterMissingContractsAsync()` inserts contract definitions the periodicals feed never delivered (e.g. solo contracts), row-only, no automation side effects |
| Query counting | `QueryCountingInterceptor` feeds `RuntimeMetrics.DbQueries` |

Composite keys (`OnModelCreating`): `UserCoopXref` (UserId, CoopId, EggIncId), `UserSnapShot` (UserId, Date, EggIncID), `UserSeasonProgress` (EggIncId, SeasonId), `GuildContract` (ContractID, GuildID, League), `TemporaryRole` (UserId, RoleId, Created), `UserCsHistoryEntry` (CoopIdentifier, ContractIdentifier, EggIncId), `DBCustomEgg` (Identifier), `ApiKeyDailyUsage` (ApiKeyId, Date). Notable indexes: unique `ApiKey.KeyHash`, `UserCoopXref` (CreatedOn, JoinedCoop), filtered `Coop` (GuildId, ContractID, League) on not finished/deleted/archived.

### In-memory caches

| Cache | File | Refresh |
|---|---|---|
| `DatabaseCache` | `Database/DatabaseCache.cs` | Users every 60s (incremental: only rows with `LastModified` past the last sweep are re-read), active coops every 5min; refreshers in `DatabaseCacheRefreshServices.cs` extend `PeriodicBackgroundService` |
| `CoopStatsCache` | `Database/CoopStatsCache.cs` | Singleton per-guild/per-contract stats snapshot (`ContractStats`/`ServerStats`); refreshed externally (Bot job, every 3 min) |
| `CoopAssignmentLookup` | `Database/CoopAssignmentLookup.cs` | Singleton (userId, contractId) -> assigned-not-joined coops; rebuilt every 3 min by `CoopAssignmentLookupRefreshService`; `Get` returns null on a miss and callers fall back to a DB query, so a stale prune is never a wrong answer |

"Active coop" for the cache means not `Finished`, not `DeletedChannel`, not `ThreadArchived`; `ActiveCoopsWithFiveMinuteDelay()` serves the last full snapshot.

### Storage patterns

| Pattern | Used for |
|---|---|
| MessagePack + LZ4 (`MessagePackCompression.Lz4BlockArray`, `DBUser.lz4Options`) | `DBUser`: accounts (`_contractRegistrationByte`), ship DMs, coop settings; `UserCoopXref`: last status, sleep tracking, coop settings; `UpcomingContract` user registers; `NasaApod` posted-to list; `DBCustomEgg` icon/modifiers |
| GZip JSON | `Coop.LastStatusUpdate` (`ContractCoopStatusResponse` serialized to JSON, GZip into `_StatusCompressed`); the setter skips the column rewrite when bytes are unchanged, the hottest write on Coops during launches |
| Plain JSON strings (Newtonsoft) | `DBContract` goals/`_response`, `Guild` channel details/coop settings/event customizations/FAQ topics/overflow servers, `SeasonInfo.GoalsJson`, `UserSnapShot.VirtueStatsJson` |

Do not add a new serialization format; reuse one of these.

### Entities (`Database/Entities/`, 32 files)

| Entity (file) | Purpose | Key fields |
|---|---|---|
| `DBUser` (`User.cs`) | Registered Discord user | `DiscordId`, `EggIncAccounts` (MessagePack blob), `Demerits`/`Merits` (+ `DemeritsGiven`/`MeritsGiven`), `CustomCoopName`/`ExpireCustomCoopName`, `OnBreakSince`/`NextBreakExpire`, `SkipNoPE`/`SkipNoArtifacts`/`SkipNoPiggyDouble`, ship-DM prefs, `HighestAnnouncedOom`, `DMSBlocked`, `Banned`, `StaleBackup`, denormalized `Usernames`/`EIDs` |
| `EggIncAccount` (nested in `User.cs`) | One linked game account | See MessagePack warning below |
| `Guild` | Discord server config | `DiscordSeverId`, JSON blobs (channel details, coop settings, event customizations, FAQ topics, overflow servers), `[GuildConfig]`-decorated toggles/lists/numbers driving `/a configure`, `GuildChannelType` enum (channels, categories, roles), `GuildCoopSetting` enum (9 ping types) |
| `DBContract` (`Contracts.cs`) | Contract definition | `ID`, `Name`, `goals`/`Rewards` JSON, `_response` (full `Ei.Contract` JSON, lazy `Details`), `MaxUsers`, `length_seconds`, `cc_only`, `GoodUntil` |
| `Coop` (`Coops.cs`) | One co-op run | `ContractID`, `Name`, `CurrentUsers`/`MaxUsers`, `Status` (`CoopStatusEnum`), `League` (uint) + `AnyLeague`, `GuildId`/`OverflowGuildId`, `ThreadID`/`ThreadParentChannel`/`ThreadArchived`, `_StatusCompressed`, `ProjectedFinish`, `CreatorID` (`TestSeedCreatorId` sentinel), helpers `FinishedOrFailed()` etc. |
| `UserCoopXref` | User-coop join table | Composite key + `JoinedCoop`, `WasAssigned`, `Starter`, `NoDemerit`, `Score`/`RunningScore`, sleep tracking, per-coop `CoopSetting`, tachyon-deflector flags, join warnings |
| `UserCoopStatus` | Per-user status snapshot in a coop | `CoopId`, `EggIncId`, `Total`, `Rate`, `SleepingWarning` |
| `GuildContract` | Contract instance per guild+league | Composite key, `DiscordChannelId`, `NumberOfCoops`, `Starters`, `Status`, `BoardingGroup`, `ReadyToScore` |
| `Demerit` / `Merit` | Discipline / commendation records | `UserId`, nullable `AdminUserId`, `Reason`; `Demerit` adds `Permanent`, `ContractID`, `Details` |
| `DBEvent` (`Event.cs`) | In-game event | `Identifier`, `Ends`, `Type`, `Multiplier`, `MessageIds`, `CcOnly` |
| `EventCustomization` | Per-event embed styling + notifications | `Type`, `Color`, `Fields`, `_settings` (guild notification rules) |
| `Donation` | Stripe donation record | `UserId`, `Amount`, `Type`, `When` |
| `DBCustomEgg` (`CustomEgg.cs`) | Custom egg definitions | `Identifier` key, icon/modifier blobs, emoji refs |
| `GlobalLeaderboardUser` / `GlobalLeaderboardCoop` | Global leaderboard crawl state | `EggIncId`, `earnings_bonus`, `soul_eggs`, `eggs_of_prophecy`, `DegreeOfSeperation`; coop rows track `Checked`/`CheckFailed` |
| `UserSnapShot` | Daily per-account history | Composite key, EB/SE/PE, `Prestiges`, `EggsOfTruth`, `VirtueStatsJson` |
| `UserCsHistoryEntry` | Contract score history | Composite key, `Cxp`, `Created` |
| `TemporaryRole` | Time-limited Discord role | Composite key, `Expires`, `Reason`, `IsRemoved` |
| `ExpiringShell` (`ExpringShell.cs`) | Limited-time shop items | `Identifier`, `Expires`, `Price`, `AssetType`, `MessageIds` |
| `AutomationLog` | Job execution audit | `Type`, `StartTime`/`EndTime`, `Skipped` |
| `UpcomingContract` | Pre-announced contract + user sign-ups | `ContractId`, `GuildID`, `TargetDate`, `IsLeggacy`, `_userRegs` blob |
| `FAQTopic` | FAQ entry | `InternalId`, `Name`, keywords, `Weight`, `Explanation`, `StaffOnly`/`PalaceOnly`, subscribed guilds |
| `RankupMessage` | Rank-up announcement text | `GuildId`, `GroupBaseOom` (-1 = global pool), `Text`, `Weight`, `PalaceOnly`, subscribed guilds |
| `SeasonInfo` / `UserSeasonProgress` | Contract seasons + per-account CXP progress | `SeasonInfo`: `Id`, `StartTime`, `GoalsJson`; progress keyed (EggIncId, SeasonId) with `TotalCxp`, `StartingGrade` |
| `ApiKey` / `ApiKeyRequestLog` / `ApiKeyDailyUsage` | Site API key auth + usage tracking | `KeyHash` (unique), `GuildId`, `Revoked`; request log keeps endpoint/IP/success; daily usage keyed (ApiKeyId, Date) |
| `ResearchCostSubmission` | Crowd-sourced research cost data | `ID`, `Level`, `Cost`, `UserId` |
| `NasaApod` | NASA APOD cache | `DateString` key data, media URLs, posted-to blob |
| `ApplicationUser` | Identity user for the Site | `IdentityUser` + `DarkMode` |
| `GuildConfigAttribute.cs` | Not an entity: `[GuildConfig]` attribute + reflection helper marking user-configurable `Guild` props |
| `ILastModified` (`Database/ILastModified.cs`) | Timestamp interface consumed by the context |

`CoopStatusEnum`: `ManualWaitingOnCreation` (1), `WaitingOnCreation` (2), `WaitingOnThread` (3), `WaitingOnStarter` (10), `WaitingOnAssigned` (11), `AllAssignedJoined` (12), `Full` (13), `Completed` (14), `CompletedAllCheckIn` (15), `Failed` (-1).

### EggIncAccount MessagePack keys (warning)

`EggIncAccount` is `[MessagePackObject]` with explicit integer keys serialized into `DBUser._contractRegistrationByte`. Keys currently run 0-44 with gaps (21, 22, 31, 32 unused): `[Key(0)]` Name, `[Key(1)]` Id, `[Key(2)]` OnBreakUntil, `[Key(8)]` LastGrade, `[Key(10)]` Backup (`CustomBackup`), `[Key(18)]` DeviceID, `[Key(19)]`/`[Key(20)]` subscription, `[Key(23)]` UltraGroup, ..., `[Key(44)]` Assignment (`AssignmentSettings`, itself a keyed MessagePack object).

Never renumber or repurpose a key, and never change a field's type without a compatibility path; stored blobs for every user would deserialize wrong. New fields get the next free key and must tolerate the default value (old blobs deserialize with it). The `EggIncAccounts` getter performs lazy read-side migrations (legacy JSON fallback, assignment-settings migration, partial-blob healing) and flags the row for rewrite via `UpdateAccounts()`.

### Key entity methods

| Type | Methods |
|---|---|
| `DBUser` | `EggIncAccounts` getter (lazy deserialization + read-side migrations), `UpdateAccounts()` (reserialize blob, refresh denormalized `Usernames`/`EIDs`, returns changed flag), `FromAccountColumns()` (lightweight projection for id-only scans), `IsFreshEgg()`, `AddName()`, `RemoveID()`, `UpdateNameAndId()`, `UpdateUserBreak()`, `UpdateDMStatus()`, static `MatchRewards()`/`MatchLastReward()` |
| `EggIncAccount` | `GetGroup(bool ultra)`, `SetBreak()`, `GetGrade()` (backup grade may only catch `LastGrade` up, never demote; real demotions come from the API grade), `HasActiveSubscription()`, `UpdateSubscriptionFromCustomBackup()` |
| `Guild` | `HasChannel()`, `GetChannelId()`, `GetCoopSetting()`, `IsLockedAndEnabled()`, `IsLockedAndDisabled()` |
| `Coop` | `FinishedOrFailed()`, `FinalizedFinishedOrFailed()`, `FinishedOrFailedOrExpired()` |

### CustomBackup (`Database/CustomBackup.cs`)

`[MessagePackObject]` snapshot of a player backup built from the protobuf `Ei.Backup` (constructor takes the raw backup plus cached contracts). Carries farms, prestige/PE/SE stats, artifact inventory and sets, space missions, virtue (leggacy) progress, subscription info, and computed helpers (`GetMostRecentContractGrade()`, `GetAvailableArtifacts()`, PE breakdowns). Same MessagePack key rules as `EggIncAccount` apply.

## Egg Inc API client (`EggIncAPI/`)

Protobuf over HTTP, not gRPC: requests are base64-encoded protobuf POSTed as a `data=` form field to `https://www.auxbrain.com/`; responses are base64 protobuf, sometimes zlib-compressed inside an `AuthenticatedMessage` wrapper. Every call uses a fresh `HttpClient` with a 30s timeout (the live API sometimes accepts and never responds) and is counted into `RuntimeMetrics` via `PostCounted`.

| File | Contents |
|---|---|
| `EggIncApi.cs` | `static partial class` core: version triple, endpoint registry (`Endpoints` map + `ResolveEndpoint`), payload building, `Post`/`PostResult`/`Send`, `ParseTolerant`, `GetFromAuthenticatedMessage`, hashing |
| `EggIncApi.Ei.cs` | `ei/` + `ei_ctx/` endpoints: periodicals, coop status, first contact, contracts info/archive, seasons, player info, `GetBackupAsync` |
| `EggIncApi.EiSrv.cs` | `ei_srv/` endpoints: `GetUserSubscription` (`ei_srv/subscription_status/{id}`) |
| `EggIncApiSecrets.cs` | Salt accessor (`Salt`, `IsSaltAvailable`) backed by `SecretsHelper.ApiSalt` |
| `ApiResult.cs` | `ApiResult<T>` success/error wrapper returned by the `*Result`/`*Async` methods |
| `ProtobufUtf8Sanitizer.cs` | Detects invalid-UTF8 parse failures and sanitizes the payload so `ParseTolerant`/`MergeTolerant` can retry (some live backups carry bad bytes) |

Key endpoints and methods:

| Method | Endpoint | Notes |
|---|---|---|
| `GetPeriodicalsAsync` | `ei/get_periodicals` | Events/contracts feed; authenticated response |
| `ValidateVersionsAsync` | `ei/get_periodicals` | Probes a candidate version triple without mutating globals |
| `GetCoopStatus` / `GetCoopStatusBot` | `ei/coop_status` / `ei/coop_status_bot` | Repairs `[departed]` participant names from xref history |
| `FirstContact` | `ei/bot_first_contact`, fallback `ei/first_contact_secure` | Prefers the unsigned bot endpoint; signed fallback only runs when a salt is configured |
| `GetBackupAsync` | wraps `FirstContact` | Returns `ApiResult<CustomBackup>` |
| `GetContractsInfoAsync` | `ei_ctx/get_contracts_info` | Signed |
| `GetContractsArchive` | `ei_ctx/get_contracts_archive` | Unsigned request, authenticated response |
| `GetSeasonInfosAsync` | `ei_ctx/get_season_infos_v2` | Signed |
| `GetContractPlayerInfo` | `ei_ctx/get_contract_player_info` | Signed |
| `GetUserSubscription` | `ei_srv/subscription_status/{id}` | GET-style POST, no body |

Generic dispatch: `Post<TResp, TReq>` / `PostResult<TResp, TReq>` / `Send<T>` resolve an `EndpointDescriptor` (path, header profile, Rinfo mode, sign request, authenticated response) from the request type:

| Request type | Endpoint |
|---|---|
| `JoinCoopRequest` | `ei/join_coop` |
| `GetPeriodicalsRequest` | `ei/get_periodicals` |
| `ContractsInfoRequest` | `ei_ctx/get_contracts_info` (signed) |
| `CreateCoopRequest` | `ei/create_coop` |
| `UpdateCoopPermissionsRequest` | `ei/update_coop_permissions` |
| `ContractCoopStatusUpdateRequest` | `ei/update_coop_status` |
| `ConfigRequest` | `ei/get_config` |
| `KickPlayerCoopRequest` | `ei/kick_player_coop` (fire-and-forget via `Send`) |
| `BasicRequestInfo` | Resolved by response type: `ContractPlayerInfo` -> `ei_ctx/get_contract_player_info` (signed), `MyContracts` -> `ei_ctx/get_contracts_archive` |

Authentication: some endpoints require the request wrapped in an authenticated envelope whose signing salt comes from a runtime secret (Docker secret `egg_inc_api_salt` / `ConnectionStrings:ApiSalt`). Without the secret, `EggIncApiSecrets.IsSaltAvailable` is false and those endpoints are disabled (they log once and return failure); everything unsigned keeps working. Response decoding never needs the salt.

Versions: `ClientVersion` / `AppVersion` / `AppBuild` are static fields that must track the live game client; stale values get requests rejected. `SetVersions()` is the single mutation point, driven at runtime by the `/a setversions` staff command and the `UpdateApiVersionsMessage` bus broadcast. Not persisted across restarts.

## Contracts and assignment (`Contracts/`)

| Path | Responsibility |
|---|---|
| `OrganizeCoops.cs` | Co-op assignment orchestration: builds coops from eligible players, grade/group handling |
| `AssignmentEngineFilter.cs` | Bridges the rule engine into the pipeline: filters candidate accounts in place, records exclusion reasons |
| `PotentialCoop.cs` / `PlayerGradeDetails.cs` | Assignment working models |
| `Assignment/` | Pure rule engine: `AssignmentEvaluator` (gate -> force -> include passes, verbose diagnostic mode for the site what-if tool), `AssignmentRuleSet`, `Rules/` (gate, force, include, seasonal rules behind `IAssignmentRule`), `Facts/` builders producing `AccountFacts`/`ContractFacts`, `AssignmentSettings` (MessagePack blob stored at `EggIncAccount` Key 44), `AssignmentSettingsMigration` (one-shot migration from legacy scalar keys), `AssignmentDecision`, `RewardMatch`, `SeasonalPeProgress`, `ContractSettingField` |

`Helpers/CreateCoopsV2.cs` creates the actual coops/channels (primary vs overflow channel caps). `Coops/ArtifactCombos.cs` computes best artifact loadouts (shipping/laying combos, optional stone reslotting) for tachyon suggestions.

## Services (`Services/`)

| Service | Purpose |
|---|---|
| `DiscordHostedService` | Composition wrapper holding a `DiscordSocketClient` (gateway) plus a `DiscordRestClient`; forwards guild/channel/user lookups, resolves configured channels/categories from `Guild` config, `RestartAsync()` |
| `PeriodicBackgroundService` | Abstract `BackgroundService` base: optional initial delay, non-overlapping `PeriodicTimer` loop around `DoWorkAsync` |
| `DiscordQueueService` / `IDiscordQueue` / `DiscordQueueOptions` | Priority write queue for Discord operations: HIGH tier for interaction responses, LOW for background jobs; `Channel<T>`-backed dynamically scaled worker pools, caller-info tags on every item for error attribution, Bugsnag reporting; options bound from the `DiscordQueue` config section |
| `RuntimeMetrics` | Static interlocked counters (DB queries, API calls/failures, commands, Discord ops) surfaced by `/a sysload` and the metrics publisher |
| `BotLogger` | Guild-log channel writer plus boarding-group status embeds (`AddBoardingGroup`, `RefreshBoardingGroup`, `MarkAssigned`) |

## MassTransit consumers (`Consumers/`)

| Consumer / message | Behavior |
|---|---|
| `ExpireCacheConsumer` (`ExpireCacheMessage`) | Removes a key from `IMemoryCache` cross-process |
| `RestartConsumer` (`RestartMessage`) | Sets exit code and stops the host |
| `ShutdownConsumer` (`ShutdownMessage`) | Stops the host only when the message came from another process with a matching build configuration |
| `UpdateApiVersionsConsumer` (`UpdateApiVersionsMessage`) | Applies a broadcast version triple via `EggIncApi.SetVersions` |
| `BotMetricsSnapshotMessage` | Message type only (published by the Bot every 15s, consumed by the Site into `bot_*` Prometheus gauges): process/GC stats, gateway latency, queue depths, `RuntimeMetrics` counters. Carries a shared bus secret for validation |

## Helpers (`Helpers/`)

| Domain | Files |
|---|---|
| Game data / math | `EggIncStatics`, `EggIncArtifacts`, `ArtifactHelpers`, `EggIncHabSpace`, `Research`, `CraftHelper`, `Prefarm`, `VirtueHelper`, `MissionHelpers` (ship emoji, tank capacity, `NeedsFuel`), `Colleggtibles`, `ColleggtibleHelper`, `ContractHistory`, `UserFarmDetails`, `SeasonalPeOption`, `RedoLeggacyOption` |
| Ranks | `RankRegistry` (canonical 52-rank registry from `farmer-ranks.json`), `SIPrefix` (EB -> rank name, derives from `RankRegistry`) |
| Assignment / coops | `ContractSettingsHelpers`, `CreateCoopsV2`, `GradeSync` (grade update guards), `KnownIds` |
| Accounts / API | `AccountRefresh`, `SubscriptionHelper`, `SiteApiClient` (bot -> site internal base URL, env override with public fallback), `EIIDScreenShots` (Tesseract OCR of EI ID screenshots) |
| Discord | `DiscordHelpers`, `DiscordMessageSplitter`, `DiscordSafe`, `FixedWidthTable`, `RoleToggle`, `ShipReturnDmBuilder`, `Discord/` (`InteractionExtensions` in `EGG9000.Common.Helpers.Discord`, `EmbedHelpers`, `ChannelHelper`, `DiscordRest`, `OverflowSyncing`, `ComponentsV2/` builder extensions) |
| Messaging / text | `MessageFormatter` (`{{...}}` substitution for FAQ and rank-up text), `RankupMessageHelper`, `RankupMessageSeed`, `FAQHelper`, `EditFaqValidation`, `EventHelpers`, `NasaHelper`, `BotText`, `Words`, `Colors`, `StringExtensions`, `StringPadBothExtension`, `TimespanStringParser` |
| Imaging | `ArtifactImaging/` (`ArtifactDisplay` shared name/rarity/effect labels + tooltip markup, `ArtifactOverlayManifest` percent-coord hotspot DTOs), `AfxSets/` (artifact-set image build/render/hash + explorer links), `IRenderConfig` |
| Infra | `DockerSecretsHelper` (reads `/run/secrets/`), `Secrets` (`SecretsHelper` config/secret accessors), `DBHelpers`, `BuildConfig`, `ArgumentsHelper`, `CustomContractResolver` (JSON contract resolver used by coop status serialization) |

Also: `Extensions/` (enum, `IEnumerable`, `uint` extensions), `Factories/` (`StaticLoggerFactory`, `TimingsFactory`), `Mocks/` (`PublishEndpointMock` for bus-less runs).

## JsonData (embedded game statics)

`EmbeddedResource<T>` is the shared lazy loader (thread-safe `Lazy<T>` over a manifest resource; `EmbeddedResource.Json<T>` / `.Csv<T>` factories). Each data class exposes a `Get()` accessor. Embedded resources: `eiafx-data.json`, `eiafx-config.json`, `ei-statics.json`, `researches.json`, `ei-epic-research.json`, `farmer-ranks.json`, `coop-words.json`, `ArtifactEmoji.json`, `curiosity_research.csv`.

## Proto (`Proto/`)

- `ei.proto` (package `ei`, proto2): the Egg Inc game schema - `Backup`, `Contract`, `ContractCoopStatusResponse`, `EggIncFirstContactResponse`, `AuthenticatedMessage`, missions, artifacts.
- `common.proto`: small shared types.
- Both compile at build via `Grpc.Tools` (`GrpcServices="Client"`); generated C# lands in `obj/`, not source. Only regenerate/change on a real schema change, and keep field numbers stable.
- `abb.proto`, `ei.pb`, `InterceptedProtos.txt`, `protocapturesjson.chlsj` are reference captures, not compiled.
- `EiExtensions.cs` is hand-written extension methods over the generated types.

## Migrations (`Migrations/`)

Single append-only migrations directory for the shared `ApplicationDbContext`; the Site's design-time setup points `MigrationsAssembly` here. History was reset to one `InitialCreate` baseline during the SQL Server to PostgreSQL port. Never edit an existing migration; add a new one. Migrations auto-apply on startup in Release builds only (Bot and Site both call `Database.Migrate()`; EF's advisory lock serializes concurrent applies).

## Consumption notes

- Both apps register Common services in their own composition roots; Common types use constructor injection and hold no static service state (statics are limited to pure helpers, `RuntimeMetrics` counters, and the API version triple).
- Caches (`CoopStatsCache`, `CoopAssignmentLookup`) take `IDbContextFactory<ApplicationDbContext>` so they can open short-lived contexts off the request path.
- `PeriodicBackgroundService` is the base for every scheduled service here and in the Bot's `Automated/` jobs; ticks never overlap because the next `WaitForNextTickAsync` only resumes after `DoWorkAsync` returns.

## Gotchas

- MessagePack blobs (`EggIncAccount`, `CustomBackup`, `AssignmentSettings`, xref blobs): integer keys are a wire contract. See the warning above.
- GZip coop-status blobs: shape changes to `ContractCoopStatusResponse` serialization can silently fail to deserialize old rows.
- API version triple is in-memory only; a restart reverts to the compiled defaults until re-set.
- `DateTimeOffset.Now` in LINQ predicates is unsafe on Npgsql; the converter/interceptor pair covers column writes and parameters, but prefer `UtcNow` everywhere.
- Screenshot ID reading (`EIIDScreenShots`) matches glyphs rendered from `Fonts/always together.otf` via SixLabors.Fonts; the consuming app must ship the font. No Tesseract/tessdata dependency.
