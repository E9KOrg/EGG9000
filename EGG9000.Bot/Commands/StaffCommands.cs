using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Interactions;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.CommonTypes.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using static EGG9000.Common.Services.DiscordHostedService;

namespace EGG9000.Bot.Commands {
    public static class StaffCommands {

        public enum MarkCleanOption {
            [ChoiceDisplay("Artifacts")] Artifacts = 0,
            [ChoiceDisplay("Crafting XP")] CraftingXP = 1,
            [ChoiceDisplay("MER")] MER = 2,
            [ChoiceDisplay("Time Cheats")] TimeCheats = 3
        }

        public enum MarkDirtyOption {
            [ChoiceDisplay("Artifacts")] Artifacts = 0,
            [ChoiceDisplay("Crafting XP")] CraftingXP = 1,
            [ChoiceDisplay("MER")] MER = 2,
            [ChoiceDisplay("Time Cheats")] TimeCheats = 3,
            [ChoiceDisplay("All")] All = 4,
        }

        private class RemoveCleanUser {
            public EggIncAccount Account { get; set; }
            public DBUser User { get; set; }
        }

        static async internal Task _afs(SocketInteraction command, ApplicationDbContext db, SocketGuildUser discUser, bool showInChannel) {
            await command.DeferAsync(ephemeral: !showInChannel);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == discUser.Id);

            var sb = new StringBuilder();

            foreach(var account in user.EggIncAccounts.Where(a => a.Backup is not null).ToList()) {
                sb.Append($"For {account.Backup?.UserName ?? "(No Name)"}: {ArtifactHelpers.GetArtifactFairnessScoreString(account.Backup.ArtifactHall)}\n");
            }

            await command.RespondAsyncGettingMessage(sb.ToString(), ephemeral: !showInChannel);
        }
    }

    public class StaffModule(IDbContextFactory<ApplicationDbContext> dbFactory, DiscordSocketClient client) : E9KModuleBase(dbFactory) {
        private readonly DiscordSocketClient _client = client;

        [SlashCommand("clearcustomeggs", "Clear ALL custom eggs from the DB, and remove Emoji.")]
        [StaffOnly(StaffTier.Admin)]
        [DefaultMemberPermissions(GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles)]
        public async Task ClearCustomEggs() {
            await Context.Interaction.DeferAsync();

            var customEggs = await Db.GetCustomEggsAsync();

            foreach(var egg in customEggs) {
                // DEV9K Overflow Server in dev/debug, Cluckingham Overflow 4 in release
                var emojiServer = (BuildConfig.IsDev9002 || BuildConfig.IsDebug)
                    ? _client.GetGuild(1130233910966620290)
                    : _client.GetGuild(1147264073659064420);
                if(emojiServer != null) {
                    var emote = await emojiServer.GetEmoteAsync(egg.EmojiId);
                    await emojiServer.DeleteEmoteAsync(emote);
                }

                Db.CustomEggs.Remove(egg);
            }
            await Db.SaveChangesAsync();
            Db._cache.InvalidateCustomEggs();

            await Context.Interaction.ModifyOriginalResponseAsync(async r => r.Content = $"Size before: {customEggs.Count}\nSize after: {(await Db.GetCustomEggsAsync()).Count}");
        }

        [SlashCommand("as", "Log a Message")]
        [StaffOnly(StaffTier.Admin)]
        [DefaultMemberPermissions(GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles)]
        public async Task AS([Summary("message")] string message, [Summary("channel")] SocketChannel channel = null, [Summary("replyto", "Message ID to reply to")] string replyto = null) {
            try {
                if(channel == null) {
                    if(replyto == null) await Context.Channel.SendMessageAsync(message);
                    else await Context.Channel.SendMessageAsync(text: message, messageReference: new MessageReference(ulong.Parse(replyto)));
                } else {
                    if(replyto == null) await ((SocketTextChannel)channel).SendMessageAsync(message);
                    else await ((SocketTextChannel)channel).SendMessageAsync(text: message, messageReference: new MessageReference(ulong.Parse(replyto)));
                }
                await Context.Interaction.RespondAsyncGettingMessage("Sent", ephemeral: true);
            } catch(Exception ex) {
                await Context.Interaction.RespondAsyncGettingMessage($"There was an error running this command: {ex.Message}", ephemeral: true);
            }
        }

        [SlashCommand("disable", "Disable user, user will not be assigned to co-ops until re-enabled")]
        [StaffOnly(StaffTier.FarmHand)]
        [DefaultMemberPermissions(GuildPermission.CreatePrivateThreads)]
        public async Task Disable([Summary("user")] SocketUser user) {
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = true;
            await Db.SaveChangesAsync();

            await Context.Interaction.RespondAsyncGettingMessage($"{user.Mention} is disabled.");
        }

        [SlashCommand("enable", "Re-enable user")]
        [StaffOnly(StaffTier.CluckingCoordinator)]
        [DefaultMemberPermissions(GuildPermission.ManageChannels)]
        public async Task Enable([Summary("user")] SocketUser user) {
            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = false;
            await Db.SaveChangesAsync();

            await Context.Interaction.RespondAsyncGettingMessage($"{user.Mention} is enabled.");
        }
    }

    public partial class AdminModule {

        [SlashCommand("afs", "Determine a user's artifact fairness score")]
        public Task AFS([Summary("user")] SocketGuildUser user, [Summary("showinchannel")] bool ShowInChannel = false) {
            return StaffCommands._afs(Context.Interaction, Db, user, ShowInChannel);
        }

        [SlashCommand("selectroleusers", "Select X random users with Y role")]
        [StaffOnly(StaffTier.FarmHand)]
        public async Task SelectRoleUsers([Summary("numberofusers")] int numberOfUsers,
            [Summary("role", "Role the user(s) should have")] SocketRole role, [Summary("role2", "Second role [...]")] SocketRole role2 = null, [Summary("role3", "Third role [...]")] SocketRole role3 = null,
            [Summary("antirole", "Role the user(s) should NOT have")] SocketRole antiRole = null, [Summary("antirole2", "Second role [...]")] SocketRole antiRole2 = null, [Summary("antirole3", "Third role [...]")] SocketRole antiRole3 = null) {
            try {
                var randomUsers = gateway.Guilds.FirstOrDefault(g => g.Id == Context.Guild?.Id).Users
                    .Where(u =>
                        u.Roles.Contains(role) && (role2 is null || u.Roles.Contains(role2)) && (role3 is null || u.Roles.Contains(role3))
                        && (antiRole == null || !u.Roles.Contains(antiRole)) && (antiRole2 == null || !u.Roles.Contains(antiRole2)) && (antiRole3 == null || !u.Roles.Contains(antiRole3))
                    )
                    .OrderBy(u => new Random().Next()).ToList();
                if(numberOfUsers != 0) randomUsers = [.. randomUsers.Take(numberOfUsers)];

                var userList = randomUsers.Count != 0 ? string.Join("\n", randomUsers.Select(u => $"<@{u.Id}>")) : "_No users found that have this filter of role(s)_\n";

                var roleCount = 1 + (role2 == null ? 0 : 1) + (role3 == null ? 0 : 1);
                var antiRoleCount = 0 + (antiRole == null ? 0 : 1) + (antiRole2 == null ? 0 : 1) + (antiRole3 == null ? 0 : 1);

                var roleDescription = $"{randomUsers.Count} Users with the role{(roleCount > 1 ? "(s)" : "")}:\n\t\t<@&{role.Id}>{(role2 is not null ? $"\n\t\t<@&{role2.Id}>" : "")}{(role3 is not null ? $"\n\t\t<@&{role3.Id}>" : "")}" +
                    $"{((antiRole != null || antiRole2 != null || antiRole3 != null) ? $"\nAnd without the role{(antiRoleCount > 1 ? "(s)" : "")}:\n\t\t{(antiRole is not null ? $"\t\t<@&{antiRole.Id}>" : "")}{(antiRole2 is not null ? $"\n\t\t<@&{antiRole2.Id}>" : "")}{(antiRole3 is not null ? $"\n\t\t<@&{antiRole3.Id}>" : "")}" : "")}\n\n";

                var tooLong = roleDescription.Length + userList.Length > 1800;
                var responseEmbedBuilder = new EmbedBuilder()
                    .WithAuthor(new EmbedAuthorBuilder().WithName("Selected Users").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).WithColor(Color.Blue)
                    .WithDescription(roleDescription + $"{(tooLong ? "_(List too large for Discord - see attached file)_\n" : userList)}");

                if(randomUsers.Count < numberOfUsers && randomUsers.Count != 0) {
                    responseEmbedBuilder.WithFooter(new EmbedFooterBuilder().WithText($"{numberOfUsers} users requested - only {randomUsers.Count} found"));
                } else if(numberOfUsers == 0 && randomUsers.Count != 0) {
                    responseEmbedBuilder.WithFooter(new EmbedFooterBuilder().WithText($"Showing all matching users"));
                }

                if(tooLong) await Context.Interaction.RespondWithFilesAsyncGettingMessage([new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(userList.Replace("<@", "").Replace(">", ""))), "RoleUsers.txt")], text: "", embed: responseEmbedBuilder.Build());
                else await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: responseEmbedBuilder.Build());

            } catch(Exception ex) {
                await Context.Interaction.RespondAsyncGettingMessage(content: "", embed: EmbedError($"Unable to parse role `{role}`.\n\n**Message**\n{ex.Message}"));
                return;
            }
        }

        [SlashCommand("pingeveryoneincoop", "Ping everyone in a co-op with a message")]
        public async Task PingEveryoneInCoop([Summary("message")] string message) {
            var coop = await Db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == Context.Interaction.ChannelId);
            if(coop == null) {
                await Context.Interaction.RespondAsyncGettingMessage($"Error finding co-op for this thread", ephemeral: true);
                return;
            }

            await Context.Interaction.RespondAsyncGettingMessage($"Pinging now", ephemeral: true);

            var pings = String.Join(" ", coop.UserCoopsXrefs.Select(x => x.User.DiscordId).GroupBy(x => x).Select(x => $"<@{x.First()}>"));

            await Context.Channel.SendMessageAsync($"{pings} {message}");
        }

        [SlashCommand("fixjoinissue", "Fix where the server doesn't show them as joined")]
        public async Task FixJoinIssue([Autocomplete(typeof(UserAccountChannelSpecificAutoComplete))][Summary("useraccount")] string useraccount) {
            await Context.Interaction.DeferAsync(ephemeral: true);

            var coop = await Db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == Context.Channel.Id || x.DiscordChannelId == Context.Channel.Id);
            if(coop == null) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel"); });
                return;
            }

            var userid = useraccount.Split("|")[0];
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbUser is null) {
                await Context.Interaction.RespondAsyncGettingMessage($"ERROR: Unable to locate DBUser entry for user");
                return;
            }

            var account = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var joinResponse = await EggIncApi.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest {

                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                UserId = account.Id,
                ClientVersion = EggIncApi.ClientVersion, Eop = 1, SoulPower = 24, Grade = (Ei.Contract.Types.PlayerGrade)coop.League, Platform = Ei.Platform.Droid, SecondsRemaining = 999, PointsReplay = false, UserName = "."
            }, account.Id);


            var updateResponse = await EggIncApi.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(new Ei.ContractCoopStatusUpdateRequest {
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Eop = 1, SoulPower = 24, UserId = account.Id, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = account.Backup.DeviceId, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                ProductionParams = new Ei.FarmProductionParams {
                    FarmPopulation = 0, Delivered = 0, Elr = 0, FarmCapacity = 0, Ihr = 0, Sr = 0
                }
            }, account.Id, true);

            await Context.Interaction.ModifyOriginalResponseAsync(x => x.Content = $"Join response- Status: {joinResponse.Status}, Banned: {joinResponse.Banned}, Success: {joinResponse.Success}");
        }
    }

    public partial class BotGroupModule {
        [SlashCommand("status", "Get the bot's status")]
        public async Task Status() {
            var command = Context.Interaction;
            var lastComplete = await Db.AutomationLogs.Where(x => x.EndTime.HasValue).GroupBy(x => x.Type).Select(x => x.OrderByDescending(y => y.EndTime).First()).ToListAsync();
            var last24 = await Db.AutomationLogs.Where(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1) && x.EndTime.HasValue).ToListAsync();
            var averages = last24.GroupBy(x => x.Type).Select(x => new { Type = x.Key, Avg = x.Average(y => y.EndTime.Value.ToUnixTimeSeconds() - y.StartTime.ToUnixTimeSeconds()) }).ToList();
            var table = new List<List<FixedWidthCell>> {new() {
                new("Name"),
                new("Avg"),
                new("Last🏁"),
                new("Attempts"),
                new("Status")
            }};
            foreach(var log in lastComplete.OrderBy(x => x.Type)) {
                var incompletes = await Db.AutomationLogs.Where(x => x.StartTime > log.EndTime && x.Type == log.Type).ToListAsync();
                var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == log.Type);
                if(service == null || service is not IUpdaterService castedService) continue;

                table.Add([
                    new(log.Type),
                    new(
                        averages.Any(x => x.Type == log.Type) ?
                        TimeSpan.FromSeconds(averages.First(x => x.Type == log.Type).Avg).Humanize().ShortenTime()
                        : ""),
                    new((DateTimeOffset.UtcNow - log.EndTime.Value).Humanize().ShortenTime()),
                    new(incompletes.Count.ToString()),
                    new(
                        castedService.Running() ?
                            (incompletes.Any(x => !x.Skipped) ?
                            $"Current run {(DateTimeOffset.UtcNow - incompletes.Last(x => !x.Skipped).StartTime).Humanize().ShortenTime()}"
                            : "Started"
                            )
                        : "Stopped"
                        )
                ]);
            }

            await command.RespondAsyncGettingMessage($"```\n{GetTable(table)}```");
        }

        [SlashCommand("restartservice", "Restart an automated service")]
        public async Task RestartService([Autocomplete(typeof(ServiceNameAutoComplete))][Summary("servicename")] string serviceName) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);
            var discordHostedService = serviceProvider.GetService<DiscordHostedService>();

            if(discordHostedService is not null && serviceName == "DiscordHostedService") {
                try {
                    await discordHostedService.RestartAsync();
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("DiscordHostedService restarted."); });
                    return;
                } catch(RestartDiscordException ex) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedExceptionFrame(ex); });
                    return;
                }
            }

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetLoadableExportedTypes())
                    .SelectMany(t => t.GetMethods())
                    .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                    .FirstOrDefault(x => x.Name == serviceName);

                if(job is null) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate a service/job with the name {serviceName}"); });
                    return;
                }

                jobService.StopJob(serviceName);
                jobService.RunJob(serviceName);
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Restarted Job.{job.DeclaringType?.Name ?? job.Name}"); });
                return;
            }

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress($"Attempting to restart {serviceName}"); });
            try {
                await service.StopAsync(new System.Threading.CancellationToken());
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                await command.RespondAsyncGettingMessage(content: "", embed: EmbedExceptionFrame(e));
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Restarted {serviceName}"); });
        }

        [SlashCommand("stopservice", "Stop an automated service")]
        public async Task StopService([Autocomplete(typeof(ServiceNameAutoComplete))][Summary("servicename")] string serviceName) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetLoadableExportedTypes())
                    .SelectMany(t => t.GetMethods())
                    .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                    .FirstOrDefault(x => x.Name == serviceName);

                if(job is null) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate a service/job with the name {serviceName}"); });
                    return;
                }

                var jobString = $"Job.{job.DeclaringType?.Name ?? job.Name}";
                if(jobService.StopJob(job.Name)) await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Stopped {jobString}"); });
                else await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to stop {jobString}"); });
                return;
            }

            if(!(service as IUpdaterService).Running()) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"The service {serviceName} is already stopped."); });
                return;
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress($"Attempting to stop {serviceName}"); });
            try {
                await service.StopAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedExceptionFrame(e); });
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Stopped {serviceName}"); });
        }

        [SlashCommand("runservice", "Run automated service now")]
        public async Task RunService([Autocomplete(typeof(ServiceNameAutoComplete))][Summary("servicename")] string serviceName) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetLoadableExportedTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsyncGettingMessage($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Ran Job.{job.DeclaringType?.Name ?? job.Name}"); });
                return;
            }


            if((service as IUpdaterService).Running()) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"The service {serviceName} is already running."); });
                return;
            }

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress($"Attempting to run {serviceName}"); });
            try {
                (service as IUpdaterService).ResetTimer();
            } catch(Exception e) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedExceptionFrame(e); });
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Ran {serviceName}"); });
        }

        [SlashCommand("startservice", "Start an automated service")]
        public async Task StartService([Autocomplete(typeof(ServiceNameAutoComplete))][Summary("servicename")] string serviceName) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetLoadableExportedTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate a service/job with the name {serviceName}"); });
                    return;
                }

                jobService.RunJob(serviceName);
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Running Job.{job.DeclaringType?.Name ?? job.Name}"); });
                return;
            }
            if((service as IUpdaterService).Running()) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedWarning($"The service {serviceName} is already running."); });
                return;
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedInProgress($"Attempting to start {serviceName}"); });
            try {
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedExceptionFrame(e); });
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Started {serviceName}"); });
        }

        [SlashCommand("dbload", "Database load, cache sizes, and process memory")]
        public async Task DbLoad() {
            var command = Context.Interaction;
            await command.DeferAsync(ephemeral: true);

            var sw = Stopwatch.StartNew();
            await Db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var proc = Process.GetCurrentProcess();
            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;

            var cacheCount = Db._cache is MemoryCache mc ? mc.Count : -1;

            var trackerEntries = Db.ChangeTracker.Entries().ToList();
            var pending = trackerEntries.Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

            var activeCoops = await Db.Coops.CountAsync(x => !x.Finished && x.CoopEnds > DateTimeOffset.UtcNow);
            var dbUsers = await Db.DBUsers.CountAsync();
            var contracts = await Db.Contracts.CountAsync();
            var events = await Db.Events.CountAsync();
            var autoLogs = await Db.AutomationLogs.CountAsync(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1));

            var rows = new List<List<FixedWidthCell>> {
                new() { new("DB Ping"), new($"{pingMs} ms", CellAlignment.Right) },
                new() { new("Working Set"), new($"{workingMb:F1} MB", CellAlignment.Right) },
                new() { new("GC Heap"), new($"{gcHeapMb:F1} MB", CellAlignment.Right) },
                new() { new("GC (0/1/2)"), new($"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}", CellAlignment.Right) },
                new() { new("Cache"), new(cacheCount >= 0 ? $"{cacheCount}" : "n/a", CellAlignment.Right) },
                new() { new("Tracked"), new($"{trackerEntries.Count}", CellAlignment.Right) },
                new() { new("Pending"), new($"{pending}", CellAlignment.Right) },
                null,
                new() { new("DBUsers"), new($"{dbUsers:N0}", CellAlignment.Right) },
                new() { new("Active Coops"), new($"{activeCoops:N0}", CellAlignment.Right) },
                new() { new("Contracts"), new($"{contracts:N0}", CellAlignment.Right) },
                new() { new("Events"), new($"{events:N0}", CellAlignment.Right) },
                new() { new("AutoLogs 24h"), new($"{autoLogs:N0}", CellAlignment.Right) },
            };

            await command.RespondAsyncGettingMessage($"```\n{GetTable(rows)}```", ephemeral: true);
        }

        [SlashCommand("coopstats", "Active Co-op Stats")]
        public async Task CoopStats() {
            var command = Context.Interaction;
            await command.DeferAsync();
            var coops = await Db.Coops.Where(x => !x.Finished && x.Status != CoopStatusEnum.Failed && x.GuildId == command.GuildId && !x.DeletedChannel && x.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();
            var stats = new StringBuilder();
            stats.AppendLine($"**Coop Threads Last Updated**");
            var coopGroups = coops.Where(x => x.LastUpdateToChannel is not null).GroupBy(x => Math.Ceiling((DateTimeOffset.UtcNow - x.LastUpdateToChannel.Value).TotalHours));

            foreach(var group in coopGroups) {
                stats.AppendLine($"<{group.Key}h: {group.Count()}");
            }
            stats.AppendLine($"\n**No Updates Yet**");

            if(coops.Count(x => x.LastUpdateToChannel is null) > 30) {
                stats.AppendLine($"Too many to list ({coops.Count(x => x.LastUpdateToChannel is null)})");
            } else {
                foreach(var coop in coops.Where(x => x.LastUpdateToChannel is null)) {
                    stats.AppendLine($"<#{coop.ThreadID}>");
                }
            }

            var messages = DiscordMessageSplitter.SplitMessage(stats.ToString(), "\n");

            await command.RespondAsyncGettingMessage(messages.First());
            for(var i = 1; i < messages.Count; i++) {
                await command.Channel.SendMessageAsync(messages[i]);
            }
        }
    }
}

