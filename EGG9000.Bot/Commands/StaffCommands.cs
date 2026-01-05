using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Services;
using EGG9000.Common.API;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Humanizer;
using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Helpers.Prefarm;
using static EGG9000.Common.Services.DiscordHostedService;

namespace EGG9000.Bot.Commands {
    public static class StaffCommands {

        public enum MarkCleanOption {
            [Discord.Interactions.ChoiceDisplay("Artifacts")] Artifacts = 0,
            [Discord.Interactions.ChoiceDisplay("Crafting XP")] CraftingXP = 1,
            [Discord.Interactions.ChoiceDisplay("MER")] MER = 2,
            [Discord.Interactions.ChoiceDisplay("Time Cheats")] TimeCheats = 3
        }

        public enum MarkDirtyOption {
            [Discord.Interactions.ChoiceDisplay("Artifacts")] Artifacts = 0,
            [Discord.Interactions.ChoiceDisplay("Crafting XP")] CraftingXP = 1,
            [Discord.Interactions.ChoiceDisplay("MER")] MER = 2,
            [Discord.Interactions.ChoiceDisplay("Time Cheats")] TimeCheats = 3,
            [Discord.Interactions.ChoiceDisplay("All")] All = 4,
        }

        [SlashCommand(Description = "Mark a potential cheater as clean", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task MarkClean(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, [SlashParam] MarkCleanOption cleantype) {
            await command.DeferAsync(ephemeral: false);
            var userid = useraccount.Split("|")[0];
            if(userid is null) await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("User id could not be found from param"); });
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbuser is null) await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID `{userid}`"); });
            var index = int.Parse(useraccount.Split("|")[1]);
            var account = dbuser.EggIncAccounts[index];

            if(account is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account `{index}` for <@{userid}> could not be found"); });
            } else {
                var identifier = string.IsNullOrEmpty(account.Name) ? account.Id : account.Name;
#if DEV9002
                await command.RespondAsync($"User account `<@{dbuser.DiscordId}>` ({identifier}) marked as having clean {cleantype}.");
#else
                await command.RespondAsync($"User account <@{dbuser.DiscordId}> ({identifier}) marked as having clean {cleantype}.");
#endif
                switch(cleantype) {
                    case MarkCleanOption.Artifacts:
                        account.AFSMarkedClean = true; break;
                    case MarkCleanOption.CraftingXP:
                        account.CraftingMarkedClean = true; break;
                    case MarkCleanOption.MER:
                        account.MERMarkedClean = true; break;
                    case MarkCleanOption.TimeCheats:
                        account.TimeCheatsMarkedClean = true; break;
                }
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();
            }
        }

        [SlashCommand(Description = "Clear ALL custom eggs from the DB, and remove Emoji.", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task ClearCustomEggs(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client) {
            await command.DeferAsync();

            var customEggs = await db.GetCustomEggsAsync();

            foreach(var egg in customEggs) {
#if DEV9002 || DEBUG
                // DEV9K Overflow Server
                var emojiServer = client.GetGuild(1130233910966620290);
#else
                // Cluckingham Overflow 4
                var emojiServer = client.GetGuild(1147264073659064420);
#endif
                if(emojiServer != null) {
                    var emote = await emojiServer.GetEmoteAsync(egg.EmojiId);
                    await emojiServer.DeleteEmoteAsync(emote);
                }

                db.CustomEggs.Remove(egg);
            }
            await db.SaveChangesAsync();
            db._cache.InvalidateCustomEggs();

            await command.ModifyOriginalResponseAsync(async r => r.Content = $"Size before: {customEggs.Count}\nSize after: {(await db.GetCustomEggsAsync()).Count}");
        }

        private class RemoveCleanUser {
            public EggIncAccount Account { get; set; }
            public DBUser User { get; set; }
        }

        [SlashCommand(Description = "Remove all 'Clean' markings from all accounts of users in this guild", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task GuildClearClean(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Remove 'Warning Sent' flags - bot will re-send detection messages")] bool removewarningsent, [SlashParam(Required = false, Description = "Clear this marking, if not provided, clear all")] MarkDirtyOption cleantype) {
            await command.DeferAsync();
            var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString()));
            if(dbGuild is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Could not determine which guild this is being run for."); });
                return;
            }

            var dbusers = await db.DBUsers.Where(u => u.GuildId == dbGuild.Id).ToListAsync();
            var accounts = dbusers.SelectMany(u => u.EggIncAccounts).ToList();
            var updatedCount = 0;
            switch(cleantype) {
                case MarkDirtyOption.Artifacts:
                    accounts = accounts.Where(a => a.AFSMarkedClean || (removewarningsent && a.AFSWarningSent)).ToList();
                    foreach(var account in accounts) {
                        account.AFSWarningSent = false;
                        account.AFSMarkedClean = false;
                        updatedCount++;
                    }
                    break;
                case MarkDirtyOption.CraftingXP:
                    accounts = accounts.Where(a => a.CraftingMarkedClean || (removewarningsent && a.CraftingWarningSent)).ToList();
                    foreach(var account in accounts) {
                        account.CraftingWarningSent = false;
                        account.CraftingMarkedClean = false;
                        updatedCount++;
                    }
                    break;
                case MarkDirtyOption.MER:
                    accounts = accounts.Where(a => a.MERMarkedClean || (removewarningsent && a.MERWarningSent)).ToList();
                    foreach(var account in accounts) {
                        account.MERWarningSent = false;
                        account.MERMarkedClean = false;
                        updatedCount++;
                    }
                    break;
                case MarkDirtyOption.TimeCheats:
                    accounts = accounts.Where(a => a.TimeCheatsMarkedClean).ToList();
                    foreach(var account in accounts) {
                        account.TimeCheatsMarkedClean = false;
                        updatedCount++;
                    }
                    break;
                case MarkDirtyOption.All:
                    accounts = accounts.Where(a =>
                        a.AFSMarkedClean || (removewarningsent && a.AFSWarningSent) ||
                        a.CraftingMarkedClean || (removewarningsent && a.CraftingWarningSent) ||
                        a.MERMarkedClean || (removewarningsent && a.MERWarningSent) ||
                        a.TimeCheatsMarkedClean
                    ).ToList();
                    foreach(var account in accounts) {
                        account.AFSWarningSent = false;
                        account.AFSMarkedClean = false;
                        account.CraftingWarningSent = false;
                        account.CraftingMarkedClean = false;
                        account.MERWarningSent = false;
                        account.MERMarkedClean = false;
                        account.TimeCheatsMarkedClean = false;
                        updatedCount++;
                    }
                    break;
                default: break;
            }

            if(updatedCount == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess("No users found to clear this marking from."); });
                return;
            }

            var accountIds = accounts.Select(a => a.Id).ToList();
            var usersToUpdate = dbusers.Where(u => u.EggIncAccounts.Any(a => accountIds.Contains(a.Id))).ToList();
            foreach(var user in usersToUpdate) {
                user.UpdateAccounts();
            }
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Modified flags on `{updatedCount}` accounts across `{usersToUpdate.Count}` users."); });
        }

        [SlashCommand(Description = "Determine a user's artifact fairness score", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task AFS(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user, [SlashParam(Required = false)] bool ShowInChannel = false) {
            return _afs(command, db, user, ShowInChannel);
        }

        private static async Task _afs(FauxCommand command, ApplicationDbContext db, SocketGuildUser discUser, bool showInChannel) {
            await command.DeferAsync(ephemeral: !showInChannel);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == discUser.Id);

            var sb = new StringBuilder();

            foreach(var account in user.EggIncAccounts.Where(a => a.Backup is not null).ToList()) {
                sb.Append($"For {account.Backup?.UserName ?? "(No Name)"}: {ArtifactHelpers.GetArtifactFairnessScoreString(account.Backup.ArtifactHall)}\n");
            }

            await command.RespondAsync(sb.ToString(), ephemeral: !showInChannel);
        }

        [SlashCommand(Description = "Log a Message", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task AS(FauxCommand command, [SlashParam] string message, [SlashParam(Required = false)] SocketChannel channel = null, [SlashParam(Required = false, Description = "Message ID to reply to")] string replyto = null) {
            try {
                if(channel == null) {
                    if(replyto == null) await command.Channel.SendMessageAsync(message);
                    else await command.Channel.SendMessageAsync(text: message, messageReference: new MessageReference(ulong.Parse(replyto)));
                } else {
                    if(replyto == null) await ((SocketTextChannel)channel).SendMessageAsync(message);
                    else await ((SocketTextChannel)channel).SendMessageAsync(text: message, messageReference: new MessageReference(ulong.Parse(replyto)));
                }
                await command.RespondAsync("Sent", ephemeral: true);
            } catch(Exception ex) {
                await command.RespondAsync($"There was an error running this command: {ex.Message}", ephemeral: true);
            }
        }

        [SlashCommand(Description = "Select X random users with Y role", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task SelectRoleUsers(FauxCommand command, DiscordSocketClient client, [SlashParam(Required = true)] int numberOfUsers, 
            [SlashParam(Required = true, Description = "Role the user(s) should have")] SocketRole role, [SlashParam(Required = false, Description = "Second role [...]")] SocketRole role2, [SlashParam(Required = false, Description = "Third role [...]")] SocketRole role3,
            [SlashParam(Required = false, Description = "Role the user(s) should NOT have")] SocketRole antiRole, [SlashParam(Required = false, Description = "Second role [...]")] SocketRole antiRole2, [SlashParam(Required = false, Description = "Third role [...]")] SocketRole antiRole3) {
            try {
                var randomUsers = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId).Users
                    .Where( u =>
                        u.Roles.Contains(role) && (role2 is null || u.Roles.Contains(role2)) && (role3 is null || u.Roles.Contains(role3))
                        && (antiRole == null || !u.Roles.Contains(antiRole)) && (antiRole2 == null || !u.Roles.Contains(antiRole2)) && (antiRole3 == null || !u.Roles.Contains(antiRole3))
                    )
                    .OrderBy(u => new Random().Next()).ToList();
                if(numberOfUsers != 0) randomUsers = randomUsers.Take(numberOfUsers).ToList();

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

                //Catch content that is too large, respond with file instead
                if(tooLong) await command.RespondWithFileAsync(new FileAttachment(new MemoryStream(Encoding.UTF8.GetBytes(userList.Replace("<@", "").Replace(">", ""))), "RoleUsers.txt"), text: "", embed: responseEmbedBuilder.Build());
                else await command.RespondAsync(content: "", embed: responseEmbedBuilder.Build());

            } catch(Exception ex) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to parse role `{role}`.\n\n**Message**\n{ex.Message}"));
                return;
            }
        }

        [SlashCommand(Description = "Add a temporary prefex for a users co-op (PrefixWord11)", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task TemporaryPrefix(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user, [SlashParam] string prefix, [SlashParam] string timespan) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to parse the timespan `{timespan}`, {ex.Message}"));
                return;
            }
            await command.DeferAsync();

            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = $""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"); });
                return;
            }

            dbuser.CustomCoopName = prefix;
            dbuser.ExpireCustomCoopName = expireTime;
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Added the co-op prefix `{prefix}` to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>. They will have any co-ops they are in named after them during that time.");
        }

        [SlashCommand(Description = "Get the bot's status", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task Status(FauxCommand command, ApplicationDbContext db, IServiceProvider serviceProvider) {
            var lastComplete = await db.AutomationLogs.Where(x => x.EndTime.HasValue).GroupBy(x => x.Type).Select(x => x.OrderByDescending(y => y.EndTime).First()).ToListAsync();
            var last24 = await db.AutomationLogs.Where(x => x.StartTime > DateTimeOffset.Now.AddDays(-1) && x.EndTime.HasValue).ToListAsync();
            var averages = last24.GroupBy(x => x.Type).Select(x => new { Type = x.Key, Avg = x.Average(y => y.EndTime.Value.ToUnixTimeSeconds() - y.StartTime.ToUnixTimeSeconds()) }).ToList();
            var table = new List<List<FixedWidthCell>> {new() {
                new("Name"),
                new("Avg"),
                new("Last🏁"),
                new("Attempts"),
                new("Status")
            }};
            foreach(var log in lastComplete.OrderBy(x => x.Type)) {
                var incompletes = await db.AutomationLogs.Where(x => x.StartTime > log.EndTime && x.Type == log.Type).ToListAsync();
                var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == log.Type);
                if(service == null || service is not IUpdaterService castedService) continue;

                table.Add([
                    new(log.Type),
                    new(
                        averages.Any(x => x.Type == log.Type) ?
                        TimeSpan.FromSeconds(averages.First(x => x.Type == log.Type).Avg).Humanize().ShortenTime()
                        : ""),
                    new((DateTimeOffset.Now - log.EndTime.Value).Humanize().ShortenTime()),
                    new(incompletes.Count.ToString()),
                    new(
                        castedService.Running() ?
                            (incompletes.Any(x => !x.Skipped) ?
                            $"Current run {(DateTimeOffset.Now - incompletes.Last(x => !x.Skipped).StartTime).Humanize().ShortenTime()}"
                            : "Started"
                            )
                        : "Stopped"
                        )
                ]);
            }

            await command.RespondAsync($"```\n{GetTable(table)}```");
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task RestartService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
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
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetExportedTypes())
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
                await command.RespondAsync(content: "", embed: EmbedExceptionFrame(e));
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Restarted {serviceName}"); });
        }

        [SlashCommand(Description = "Stop an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StopService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetExportedTypes())
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

        [SlashCommand(Description = "Run automated service now", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task RunService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetExportedTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsync($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Ran Job.{job.DeclaringType?.Name ?? job.Name}"); });
                return;
            }

            if(!(service as IUpdaterService).Running()) {
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

        [SlashCommand(Description = "Start an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StartService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            await command.DeferAsync();
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetExportedTypes())
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

        [SlashCommand(Description = "Ping everyone in a co-op with a message", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task PingEveryoneInCoop(FauxCommand command, [SlashParam] string message, ApplicationDbContext db) {
            var coop = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.ThreadID == command.ChannelId);
            if(coop == null) {
                await command.RespondAsync($"Error finding co-op for this thread", ephemeral: true); 
                return;
            }

            await command.RespondAsync($"Pinging now", ephemeral: true);

            var pings = String.Join(" ", coop.UserCoopsXrefs.Select(x => x.User.DiscordId).GroupBy(x => x).Select(x => $"<@{x.First()}>"));

            await command.Channel.SendMessageAsync($"{pings} {message}");
        }

        [SlashCommand(Description = "Fix where the server doesn't show them as joined", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task FixJoinIssue(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount, ApplicationDbContext db) {
            await command.DeferAsync(ephemeral: true);

            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.ThreadID == command.Channel.Id || x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Command can only be used in a co-op channel"); });
                return;
            }

            var userid = useraccount.Split("|")[0];
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            if(dbUser is null) {
                await command.RespondAsync($"ERROR: Unable to locate DBUser entry for user");
                return;
            }

            var account = dbUser.EggIncAccounts.OrderByDescending(x => x.Backup?.EarningsBonus).ToList()[int.Parse(useraccount.Split("|")[1])];

            var joinResponse = await EggIncAPI.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest {

                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                UserId = account.Id, 
                ClientVersion = EggIncAPI.ClientVersion, Eop = 1, SoulPower = 24, Grade = (Ei.Contract.Types.PlayerGrade)coop.League, Platform = Ei.Platform.Droid, SecondsRemaining = 999, PointsReplay = false, UserName = "."
            }, account.Id);


            var updateResponse = await EggIncAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(new Ei.ContractCoopStatusUpdateRequest {
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Eop = 1, SoulPower = 24, UserId = account.Id, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = account.Backup.DeviceId, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                ProductionParams = new Ei.FarmProductionParams {
                    FarmPopulation = 0, Delivered = 0, Elr = 0, FarmCapacity = 0, Ihr = 0, Sr = 0
                }
            }, account.Id, true);

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Join response- Status: {joinResponse.Status}, Banned: {joinResponse.Banned}, Success: {joinResponse.Success}");
        }
    }
}

