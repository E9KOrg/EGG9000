#if !RELEASE
using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    /// <summary>
    /// DEV-only test harness. Compiled out entirely in Release (the whole file is
    /// behind <c>#if !RELEASE</c>), so these commands never register on the prod bot.
    public static class TestSuiteCommands {
        public const string SeedPrefix = "TESTSEED-";

        private static string ShortId() => Guid.NewGuid().ToString("N")[..8];

        [SlashCommand(Description = "[DEV] Seed fake coops for a contract so stats populate", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task SeedCoops(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client,
            [SlashParam(Description = "Contract to attach fake coops to", AutocompleteHandler = typeof(StaffContractAutoComplete))] string contractid,
            [SlashParam(Description = "How many to create")] int count,
            [SlashParam(Required = false, Description = "Also create real Discord threads for them (default no)")] bool createthreads = false) {
            await command.DeferAsync(ephemeral: true);

            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            if(contract is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No contract found with ID `{contractid}`."); });
                return;
            }
            if(count is < 1 or > 100) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Count must be 1-100."); });
                return;
            }

            var maxUsers = contract.MaxUsers > 0 ? contract.MaxUsers : 10;
            // Cycle statuses so stats show a realistic mix: active / pending / finished.
            CoopStatusEnum[] cycle = [CoopStatusEnum.AllAssignedJoined, CoopStatusEnum.WaitingOnThread, CoopStatusEnum.Completed];

            var seeded = new List<Coop>();
            for(var i = 0; i < count; i++) {
                var status = cycle[i % cycle.Length];
                var coop = new Coop {
                    ContractID = contractid,
                    Name = $"{SeedPrefix}{ShortId()}",
                    GuildId = command.GuildId ?? 0,
                    Status = status,
                    League = 0,
                    MaxUsers = maxUsers,
                    CurrentUsers = status == CoopStatusEnum.WaitingOnThread ? 0 : 1 + (i % maxUsers),
                    ThreadID = status == CoopStatusEnum.WaitingOnThread ? 0ul : (ulong)(1_000_000 + i),
                    CoopEnds = DateTimeOffset.Now.AddDays(2),
                    Created = DateTimeOffset.Now,
                    AddedFromBackup = true,
                    // Sentinel so automated services never touch these (no API, no auto-threads).
                    CreatorID = Coop.TestSeedCreatorId,
                };
                db.Coops.Add(coop);
                seeded.Add(coop);
            }
            await db.SaveChangesAsync();

            var threadNote = "";
            if(createthreads) {
                var gc = await db.GuildContracts.FirstOrDefaultAsync(c => c.GuildID == (command.GuildId ?? 0) && c.ContractID == contractid);
                if(client.GetChannel(gc?.DiscordChannelId ?? 0) is ITextChannel channel) {
                    var made = 0;
                    foreach(var coop in seeded.Where(c => c.Status != CoopStatusEnum.WaitingOnThread)) {
                        try {
                            var thread = await channel.CreateThreadAsync(coop.Name, autoArchiveDuration: ThreadArchiveDuration.OneDay, type: ThreadType.PublicThread);
                            coop.ThreadID = thread.Id;
                            made++;
                        } catch { /* best-effort in DEV */ }
                    }
                    await db.SaveChangesAsync();
                    threadNote = $" Created `{made}` real threads.";
                } else {
                    threadNote = " (Could not find the contract channel to create threads.)";
                }
            }

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Seeded `{count}` fake coops for **{contract.Name}** (`{contractid}`).{threadNote} Run `/test refreshstats` (or wait for the next tick) to see them in stats."); });
        }

        [SlashCommand(Description = "[DEV] Remove all seeded fake coops (and their assignments) in this guild", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task ClearSeed(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);

            var guildId = command.GuildId ?? 0;
            var seeded = await db.Coops.IgnoreQueryFilters().Where(c => c.GuildId == guildId && c.Name.StartsWith(SeedPrefix)).ToListAsync();
            var seededIds = seeded.Select(c => c.Id).ToList();
            var xrefs = await db.UserCoopXrefs.Where(x => seededIds.Contains(x.CoopId)).ToListAsync();

            db.UserCoopXrefs.RemoveRange(xrefs);
            db.Coops.RemoveRange(seeded);
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Removed `{seeded.Count}` seeded coops and `{xrefs.Count}` assignments."); });
        }

        [SlashCommand(Description = "[DEV] Force a CoopStatsCache refresh now", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task RefreshStats(FauxCommand command, CoopStatsCache stats) {
            await command.DeferAsync(ephemeral: true);
            await stats.RefreshAsync();
            var server = command.GuildId.HasValue ? stats.GetServerStats(command.GuildId.Value) : null;
            var summary = server is null
                ? "No stats for this guild yet (seed some coops first)."
                : $"Active contracts: **{server.ActiveContracts}**, active coops: **{server.ActiveCoops}**, pending: **{server.PendingThreads}**, players: **{server.UsersAssigned}**.";
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Refreshed CoopStatsCache.\n{summary}"); });
        }

        [SlashCommand(Description = "[DEV] Force the stats embed updater to run immediately", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task RunEmbed(FauxCommand command, IServiceProvider serviceProvider) {
            await command.DeferAsync(ephemeral: true);
            var svc = serviceProvider.GetServices<IHostedService>().OfType<IUpdaterService>()
                .FirstOrDefault(s => s.GetType().Name == nameof(CoopStatsRefreshService));
            if(svc is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("CoopStatsRefreshService not found."); });
                return;
            }
            svc.ResetTimer();
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("Triggered CoopStatsRefreshService. Embeds should update within a few seconds (needs a configured CoopStatsChannel)."); });
        }

        [SlashCommand(Description = "[DEV] Add fake API/DB metric load so /a dbload reporting can be eyeballed", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task LoadMetrics(FauxCommand command, [SlashParam(Description = "API calls to add")] int api, [SlashParam(Description = "DB queries to add")] int db) {
            RuntimeMetrics.AddApiCalls(Math.Max(0, api));
            RuntimeMetrics.AddDbQueries(Math.Max(0, db));
            await command.RespondAsync(content: "", embed: EmbedSuccess($"Added `{api}` API calls and `{db}` DB queries. Totals now API `{RuntimeMetrics.ApiCalls:N0}`, DB `{RuntimeMetrics.DbQueries:N0}`."), ephemeral: true);
        }

        [SlashCommand(Description = "[DEV] Create a fake coop for a contract and assign your account to it", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "test")]
        public static async Task AssignMe(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Contract", AutocompleteHandler = typeof(StaffContractAutoComplete))] string contractid) {
            await command.DeferAsync(ephemeral: true);

            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            if(contract is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No contract found with ID `{contractid}`."); });
                return;
            }

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == command.User.Id);
            if(dbUser is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("You have no DBUser record - register first."); });
                return;
            }

            var coop = new Coop {
                ContractID = contractid,
                Name = $"{SeedPrefix}{ShortId()}",
                GuildId = command.GuildId ?? 0,
                Status = CoopStatusEnum.AllAssignedJoined,
                League = 0,
                MaxUsers = contract.MaxUsers > 0 ? contract.MaxUsers : 10,
                CurrentUsers = 1,
                ThreadID = (ulong)(2_000_000 + Environment.TickCount64 % 1_000_000),
                CoopEnds = DateTimeOffset.Now.AddDays(2),
                Created = DateTimeOffset.Now,
                AddedFromBackup = true,
                CreatorID = Coop.TestSeedCreatorId,
            };
            db.Coops.Add(coop);
            await db.SaveChangesAsync();

            var eggIncId = dbUser.EggIncAccounts.FirstOrDefault()?.Id ?? "TEST";
            db.UserCoopXrefs.Add(new UserCoopXref {
                UserId = dbUser.Id,
                CoopId = coop.Id,
                EggIncId = eggIncId,
                // Intentionally false so that "Find my Coop" doesn't filter this out as already-joined
                JoinedCoop = false,
                CreatedOn = DateTimeOffset.Now,
                Joined = DateTimeOffset.Now,
            });
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Assigned you to fake coop `{coop.Name}` for **{contract.Name}**. 'Find my Coop' in the contract channel should now return it. Clean up with `/test clearseed`."); });
        }
    }
}
#endif
