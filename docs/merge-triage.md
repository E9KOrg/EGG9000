# fix/long-term-cleanup - Merge Triage

Per-file classification of the cleanup PR (`fix/long-term-cleanup` vs `chore/commented-code`)
to decide what can be self-merged versus what Ken should review first.

- Scope reviewed: the **real code changes** (197 files; comment-only removals are already
  split into `chore/commented-code`, and pure file deletions into `chore/removals`).
- `score` = confidence the change is correct and safe (1 = trivial/obvious, 5 = obviously fine but high-impact area).
- `risk` = regression/behavioral risk if wrong.
- Totals: **~133 QUICK-MERGE, ~62 KEN-REVIEW.**

> Most KEN-REVIEW items are not "bad", just behavior-affecting (query semantics, schema,
> auth, scheduling). They cluster into a few themes - review the **theme** once and most
> files fall out.

---

## Cross-cutting review units (review once, covers many files)

### A. Thread-only co-op migration (drop `DiscordChannelId` / `DeletedChannel`)
The single largest theme. Co-ops are now thread-only; the legacy channel-id and
deleted-channel fallbacks are removed everywhere and the columns are dropped from the DB.
**This is coherent and intentional** (entity + migration + every call site move together),
but it is irreversible at the DB layer and changes which co-ops legacy paths touch.

Decision hinge for Ken: *are all live co-ops guaranteed to have a `ThreadID`?* If yes, the
whole family is safe.

Files in this unit: `Database/Entities/Coops.cs`, the two migrations
(`20260606000000_RemoveCoopChannelFields.*`), `CoopAssignmentLookup.cs`, `DatabaseCache.cs`,
`DiscordHelpers.cs`, `ContractCommandsSlash.cs`, `CommandService.cs`, `DiscordUserService.cs`,
`CoopSettings.cs`, `DemeritCommands.cs`, `MiscCommandsSlash.cs`, `UserStatusCommands.cs`,
`AutoCompleteHandlers.cs`, `StaffCommands.cs`, `ThreadsCoopStatusUpdater.cs`,
`ContractController.cs`, `Views/Contract/Details.cshtml`, `Views/Home/Coop.cshtml`,
`Views/MyFarms/{-ContractHistory,-ShipsAndFarms,Index}.cshtml`, `Common.Test/CoopAssignmentLookup.cs`.

### B. Namespace consolidation + static-data type renames (mechanical, safe)
`EGG9000.Bot.*` helper/attribute types moved into `EGG9000.Common.*`; generated static-data
wrapper classes renamed (`Root` -> `EIAfxConfigRoot` / `EIStaticsRoot`, `EiEpicResearch` ->
`EIEpicResearch`). Pure relocation/rename - compiler-enforced, **no behavior or data change**.
Covers the bulk of QUICK-MERGE in Helpers, JsonData, Commands/Informational, and the
`_ViewImports.cshtml` / per-view `@using` adjustments.

### C. Roslyn style sweep (mechanical, safe)
Collection expressions (`[..]`), primary constructors, `var`, expression bodies, `StringComparison`
over `ToLower()`, unused-using/dead-member removal. The default class of QUICK-MERGE. Note the
~25 Identity `Account/**.cshtml.cs` files are **all** primary-ctor-only scaffolding edits.

---

## KEN-REVIEW (route to Ken first)

### Schema / migrations - HIGH (irreversible, data-loss on Up)
| file | score | note |
|---|---|---|
| Migrations/20260606000000_RemoveCoopChannelFields.cs | 5 | DROPs Coops.DiscordChannelId/DeletedChannel/FindChannelErrors/WarningForDeleteChannel + index swap. Down restores. |
| Migrations/20260605071705_cleanup.cs | 5 | DROPs 5 Guild columns (Elites/Standards/_faqTopicsJson...). Down restores. |
| Migrations/*.Designer.cs + ApplicationDbContextModelSnapshot.cs | 5 | Generated; verify they match the two migrations + EF9->10 regen. |
| Database/Entities/Coops.cs | 5 | Removes the 4 mapped columns (pairs with migration). |
| Database/Entities/Guild.cs | 5 | Removes the 5 mapped columns (pairs with migration). |

### Auth / security - HIGH
| file | score | note |
|---|---|---|
| Site/Controllers/HomeController.cs | 4 | New `[Authorize(Admin)]` on GetMessage/CheckBoost/ViewBackup; removed XFinity/Test1 endpoints; guild-scoped leaderboard fetch. Confirm intended gating. |

### Behavior / data-fetch semantics - HIGH
| file | score | note |
|---|---|---|
| Site/Controllers/MyFarmsController.cs | 5 | Deferred render + fire-and-forget background backup refresh (own DI scope/Task.Run). When/how data is fetched+persisted changed. |
| Site/Controllers/AdminController.cs | 4 | New guildDays graph series; InactivePlayers now guild-scoped (was all users); SearchID now SQL substring (was full-table). Results changed. |
| Bot/Automated/ManageOverflow.cs | 2 | New role-icon sync feature + RoleUpdated subscribe move + overflow membership rewrite. |
| Bot/Automated/ThreadsCoopStatusUpdater.cs | 2 | Drops the `DiscordChannelId==0` predicate -> widens processed co-op set (thread-only, unit A). |

### Behavior / query semantics - MED
| file | score | note |
|---|---|---|
| Database/Entities/User.cs | 4 | `EIDs` now indexes by `account.Id` (incl un-backed accounts) - affects ban/duplicate-registration checks. |
| Bot/Commands/RegisterCommandsSlash.cs | 3 | Ban/dup checks moved to `AnyAsync(EIDs.Contains)`; verify EID **casing** (old `.ToUpper()`). Possible bug. |
| Common/EggIncAPI/EggIncApi.Ei.cs | 3 | 3 hardcoded throwaway EIIDs unified to one `UserId` const - request identity change (fine for unauth endpoints, confirm). |
| Common/Helpers/EventHelpers.cs, FAQHelper.cs | 3 | Add 6h memory cache + `AsNoTracking` for palace-guild lookup - staleness up to 6h. |
| Common/Helpers/Prefarm.cs | 3 | Removes several large public methods (BackupToPreFarm/GetPrefarmsForCoop) - confirm no external callers. |
| Common/Contracts/OrganizeCoops.cs | 3 | History lookup O(n) dict rewrite (result-preserving) but in coop-assignment path. |
| Common/Database/CustomBackup.cs | 3 | Removes CustomUniversalFarm class (+ collection-expr noise). |
| Common/Database/DatabaseCache.cs | 4 | New guild/discordId lookups; drops DeletedChannel filter (unit A). |
| Common/Helpers/ChannelHelper.cs | 3 | New async `DetermineChannelTypeAsync`; verify callers. |
| Common/Services/DiscordHostedService.cs | 3 | Removes `GetAllFinishedCategories` (confirm no callers) + nullable/emoji refactor. |
| Bot/Automated/ContractUpdater.cs, EventUpdater.cs, NewContracts.cs, LeaderboardUpdates.cs, ShipReturnDM.cs | 3 | SaveChanges moved out of loops / eager materialization / tracked-entity writes - result-preserving but DB-shape/timing changes. |
| Bot/Automated/CreateCoopThreads.cs | 4 | Dropped OverflowServer capacity logic + thread-create timeout rewrite. |
| Bot/Automated/ArtifactCheaters.cs | 4 | Removed RunCraftingLevelCheck; owner now via accountOwner dict (name-collision fix). |
| Bot/Services/CommandService.cs, DiscordUserService.cs | 2 | Dispatcher / user service; coop msg/DM lookups under unit A. |
| Bot/Commands/{AutoCompleteHandlers,StaffCommands,ChasingCommand,FAQCommandSlash}.cs | 3 | Cache/DB swaps, AsSplitQuery, GetUser, pattern-match rewrites - verify equivalence. |
| Site/Controllers/ContractController.cs | 3 | AsSplitQuery (same results) + MoveUser thread-only (unit A). |
| Site/Views/Admin/Index.cshtml | 4 | New global/server graph toggle + 2 chart series; depends on AdminController `getgraphs` shape. |
| Site/Views/_ViewImports.cshtml | 3 | Drops all `EGG9000.Bot` usings; views self-import now. |

### Low-risk but flagged by reviewers (quick glance)
`StaffCoopsMessage.cs` (GZip-skip projection), `UserSnapShots.cs` (prefetch HashSet),
`UserDMsJob.cs` (async + hoist), `PlayerGradeDetails.cs` (removed GetAutoCompleteSuggestion),
`Common/Database/CoopAssignmentLookup.cs`, `EGG9000.Bot.csproj` (Grpc.Tools bump + EFCore.Design pin).

---

## QUICK-MERGE (self-merge) - by category

- **Identity scaffolding** (~25 files): all `Areas/Identity/.../*.cshtml.cs` - primary-ctor only.
- **Namespace/type renames** (unit B): Helpers/AfxSets/*, ArtifactHelpers, EggIncArtifacts,
  EggIncStatics, MissionHelpers, CraftHelper, SIPrefix, Words, FixedWidthTable, StringPad*,
  Colors, Colleggtibles, ContractHistory, SubscriptionHelper, DockerSecretsHelper,
  all `JsonData/*`, Commands/Informational/{ArtifactCommands,CraftCommand,EBHistoryCommand},
  TestSuiteCommands, AssemblyExtensions, BotLogger, the Common.Test files.
- **Dead-code/member removal** (verified building): BanCommands, ConfigureCommands,
  ContextUserCommands, NasaCommands, NewCode, Ping, ShipReturnDMSettings, ContractSettings,
  JobService, DiscordMessageSplitter, StringExtensions, BotText, EggIncHabSpace, UserFarmDetails,
  Proto/EiExtensions, EventCustomization, FormulaCommands, ArgumentsHelper, RefreshNasaApod,
  UpdateBackups, UserCxpUpdater, HandleGradeChanges, UptimeKuma, ExpireCacheConsumer.
- **Style-only**: most Database/Entities (Contracts, CustomEgg, FAQTopic, GuildContract,
  GuildConfigAttribute, UpcomingContract, UserCoopXref, UserCsHistoryEntry, ILastModified),
  CoopStatsCache, ApplicationDbContext (type inference), CreateCoopsV2, FauxCommand,
  VirtueHelper, Extensions/*, EggIncStatics, PrometheusMetricServerHostedService,
  DiscordQueueService, PeriodicBackgroundService, NewCoopChecker, CustomClaimsPrincipleFactory,
  HealthController, APIController, DonationController.
- **Config / generated / cosmetic**: `.editorconfig`, GlobalSuppressions, both `nlog.config`,
  `site.css`, `EGG9000.Common.csproj`, `EGG9000.Site.csproj` (tessdata drop + Stripe/EF pins),
  `EGG9000.Common.Test.csproj`, `Program.cs` (Bot + Site, unused-using/dead-service removal),
  edits to the 3 pre-existing migrations (unused-using only), dead-markup views
  (Day1Coops, Donation/Index, Home/{Comparison,CraftingLevelComparison,Enlightenment,GradeComparison},
  MyFarms/{-EpicResearch,-ExternalTools,-InventoryView,ResearchTest,-ArtifactCombos},
  Admin/EventCustomization).

---

## Watch-items (potential bugs to confirm regardless of bucket)

1. **RegisterCommandsSlash.cs** - EID casing: old path upper-cased; new `EIDs.Contains(eggincid)`. Confirm both sides normalize, or ban/dup detection can silently miss.
2. **User.cs `EIDs`** - now includes accounts with no backup. Confirm that is intended for ban/dup matching.
3. **MyFarmsController** background refresh - fire-and-forget Task with its own DI scope; confirm no DbContext lifetime/disposal issues under load.
4. **EventHelpers/FAQHelper** 6h cache - confirm 6h staleness is acceptable for palace-guild config.
5. **Thread-only (unit A)** - confirm every live co-op has a `ThreadID` before dropping the columns.
