#if !RELEASE
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Interactions;
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
    // DEV-only test harness. Whole file is `#if !RELEASE` so these never register on prod.
    [Group("test", "DEV test harness")]
    [DefaultMemberPermissions(GuildPermission.Administrator)]
    public class TestModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client, CoopStatsCache stats, IServiceProvider serviceProvider) : E9KModuleBase(dbFactory) {
        public const string SeedPrefix = "TESTSEED-";

        private readonly DiscordSocketClient _client = client;
        private readonly CoopStatsCache _stats = stats;
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        private static string ShortId() => Guid.NewGuid().ToString("N")[..8];

        [SlashCommand("seedcoops", "[DEV] Seed fake coops for a contract so stats populate")]
        public async Task SeedCoops(
            [Summary(description: "Contract to attach fake coops to")][Autocomplete(typeof(StaffContractAutoComplete))] string contractid,
            [Summary(description: "How many to create")] int count,
            [Summary(description: "Also create real Discord threads for them (default no)")] bool createthreads = false) {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            if(contract is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No contract found with ID `{contractid}`."); });
                return;
            }
            if(count is < 1 or > 100) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Count must be 1-100."); });
                return;
            }

            var maxUsers = contract.MaxUsers > 0 ? contract.MaxUsers : 10;
            CoopStatusEnum[] cycle = [CoopStatusEnum.AllAssignedJoined, CoopStatusEnum.WaitingOnThread, CoopStatusEnum.Completed];

            var seeded = new List<Coop>();
            for(var i = 0; i < count; i++) {
                var status = cycle[i % cycle.Length];
                var coop = new Coop {
                    ContractID = contractid,
                    Name = $"{SeedPrefix}{ShortId()}",
                    GuildId = Context.Guild?.Id ?? 0,
                    Status = status,
                    League = 0,
                    MaxUsers = maxUsers,
                    CurrentUsers = status == CoopStatusEnum.WaitingOnThread ? 0 : 1 + (i % maxUsers),
                    ThreadID = status == CoopStatusEnum.WaitingOnThread ? 0ul : (ulong)(1_000_000 + i),
                    CoopEnds = DateTimeOffset.UtcNow.AddDays(2),
                    Created = DateTimeOffset.UtcNow,
                    AddedFromBackup = true,
                    CreatorID = Coop.TestSeedCreatorId,
                };
                Db.Coops.Add(coop);
                seeded.Add(coop);
            }
            await Db.SaveChangesAsync();

            var threadNote = "";
            if(createthreads) {
                var guildId = Context.Guild?.Id ?? 0;
                var gc = await Db.GuildContracts.FirstOrDefaultAsync(c => c.GuildID == guildId && c.ContractID == contractid);
                if(_client.GetChannel(gc?.DiscordChannelId ?? 0) is ITextChannel channel) {
                    var made = 0;
                    foreach(var coop in seeded.Where(c => c.Status != CoopStatusEnum.WaitingOnThread)) {
                        try {
                            var thread = await channel.CreateThreadAsync(coop.Name, autoArchiveDuration: ThreadArchiveDuration.OneDay, type: ThreadType.PublicThread);
                            coop.ThreadID = thread.Id;
                            made++;
                        } catch { /* best-effort in DEV */ }
                    }
                    await Db.SaveChangesAsync();
                    threadNote = $" Created `{made}` real threads.";
                } else {
                    threadNote = " (Could not find the contract channel to create threads.)";
                }
            }

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Seeded `{count}` fake coops for **{contract.Name}** (`{contractid}`).{threadNote} Run `/test refreshstats` (or wait for the next tick) to see them in stats."); });
        }

        [SlashCommand("clearseed", "[DEV] Remove all seeded fake coops (and their assignments) in this guild")]
        public async Task ClearSeed() {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var guildId = Context.Guild?.Id ?? 0;
            var seeded = await Db.Coops.IgnoreQueryFilters().Where(c => c.GuildId == guildId && c.Name.StartsWith(SeedPrefix)).ToListAsync();
            var seededIds = seeded.Select(c => c.Id).ToList();
            var xrefs = await Db.UserCoopXrefs.Where(x => seededIds.Contains(x.CoopId)).ToListAsync();

            Db.UserCoopXrefs.RemoveRange(xrefs);
            Db.Coops.RemoveRange(seeded);
            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Removed `{seeded.Count}` seeded coops and `{xrefs.Count}` assignments."); });
        }

        [SlashCommand("refreshstats", "[DEV] Force a CoopStatsCache refresh now")]
        public async Task RefreshStats() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            await _stats.RefreshAsync();
            var guildId = Context.Guild?.Id;
            var server = guildId.HasValue ? _stats.GetServerStats(guildId.Value) : null;
            var summary = server is null
                ? "No stats for this guild yet (seed some coops first)."
                : $"Active contracts: **{server.ActiveContracts}**, active coops: **{server.ActiveCoops}**, pending: **{server.PendingThreads}**, players: **{server.UsersAssigned}**.";
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Refreshed CoopStatsCache.\n{summary}"); });
        }

        [SlashCommand("runembed", "[DEV] Force the stats embed updater to run immediately")]
        public async Task RunEmbed() {
            await Context.Interaction.DeferAsync(ephemeral: true);
            var svc = _serviceProvider.GetServices<IHostedService>().OfType<IUpdaterService>()
                .FirstOrDefault(s => s.GetType().Name == nameof(CoopStatsRefreshService));
            if(svc is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("CoopStatsRefreshService not found."); });
                return;
            }
            svc.ResetTimer();
            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("Triggered CoopStatsRefreshService. Embeds should update within a few seconds (needs a configured CoopStatsChannel)."); });
        }

        [SlashCommand("loadmetrics", "[DEV] Add fake API/DB metric load so /a dbload reporting can be eyeballed")]
        public async Task LoadMetrics(
            [Summary(description: "API calls to add")] int api,
            [Summary(description: "DB queries to add")] int db) {
            RuntimeMetrics.AddApiCalls(Math.Max(0, api));
            RuntimeMetrics.AddDbQueries(Math.Max(0, db));
            await Context.Interaction.RespondAsync(text: "", embed: EmbedSuccess($"Added `{api}` API calls and `{db}` DB queries. Totals now API `{RuntimeMetrics.ApiCalls:N0}`, DB `{RuntimeMetrics.DbQueries:N0}`."), ephemeral: true);
        }

        [SlashCommand("assignme", "[DEV] Create a fake coop for a contract and assign your account to it")]
        public async Task AssignMe([Summary(description: "Contract")][Autocomplete(typeof(StaffContractAutoComplete))] string contractid) {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var contract = await Db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            if(contract is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No contract found with ID `{contractid}`."); });
                return;
            }

            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == Context.User.Id);
            if(dbUser is null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("You have no DBUser record - register first."); });
                return;
            }

            var coop = new Coop {
                ContractID = contractid,
                Name = $"{SeedPrefix}{ShortId()}",
                GuildId = Context.Guild?.Id ?? 0,
                Status = CoopStatusEnum.AllAssignedJoined,
                League = 0,
                MaxUsers = contract.MaxUsers > 0 ? contract.MaxUsers : 10,
                CurrentUsers = 1,
                ThreadID = (ulong)(2_000_000 + Environment.TickCount64 % 1_000_000),
                CoopEnds = DateTimeOffset.UtcNow.AddDays(2),
                Created = DateTimeOffset.UtcNow,
                AddedFromBackup = true,
                CreatorID = Coop.TestSeedCreatorId,
            };
            Db.Coops.Add(coop);
            await Db.SaveChangesAsync();

            var eggIncId = dbUser.EggIncAccounts.FirstOrDefault()?.Id ?? "TEST";
            Db.UserCoopXrefs.Add(new UserCoopXref {
                UserId = dbUser.Id,
                CoopId = coop.Id,
                EggIncId = eggIncId,
                JoinedCoop = false,
                CreatedOn = DateTimeOffset.UtcNow,
                Joined = DateTimeOffset.UtcNow,
            });
            await Db.SaveChangesAsync();

            await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Assigned you to fake coop `{coop.Name}` for **{contract.Name}**. 'Find my Coop' in the contract channel should now return it. Clean up with `/test clearseed`."); });
        }
    }
}
#endif
