using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Helpers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Humanizer;
using MassTransit;
using MassTransit.SagaStateMachine;

using Microsoft.AspNetCore.DataProtection.XmlEncryption;
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
using System.Threading;
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
                    .Where(u =>
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
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.UtcNow);
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
            var last24 = await db.AutomationLogs.Where(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1) && x.EndTime.HasValue).ToListAsync();
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
                await command.RespondAsync(content: "", embed: EmbedExceptionFrame(e));
            }
            await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedSuccess($"Restarted {serviceName}"); });
        }

        [SlashCommand(Description = "Stop an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StopService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
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

            if(!(service as IUpdaterService).Running() && !(service as IUpdaterService).Active()) {
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
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetLoadableExportedTypes())
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

        [SlashCommand(Description = "Start an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StartService(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
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

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Join response- Status: {joinResponse.Status}, Banned: {joinResponse.Banned}, Success: {joinResponse.Success}");
        }

        // Live /a sysload sessions, keyed by the message. Tracks the section currently
        // shown (so a refresh re-renders that section) and the cancellation source for
        // the auto-refresh loop (Stop button / deleted message / 30s cap).
        private sealed class SysLoadSession {
            public CancellationTokenSource Cts;
            public string Section = "overview";
        }
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, SysLoadSession> _sysLoad = new();

        private static string FormatUptime(TimeSpan u) =>
            u.TotalDays >= 1 ? $"{(int)u.TotalDays}d {u.Hours}h {u.Minutes}m"
            : u.TotalHours >= 1 ? $"{u.Hours}h {u.Minutes}m"
            : u.TotalMinutes >= 1 ? $"{u.Minutes}m {u.Seconds}s"
            : $"{u.Seconds}s";

        // 1.0 at/below good, 0.0 at/above bad, linear between (good < bad).
        private static double HealthRange(double v, double good, double bad) =>
            v <= good ? 1 : v >= bad ? 0 : 1 - (v - good) / (bad - good);

        // Green (healthy) -> yellow -> red (unhealthy) for a 0..1 health score.
        private static Color HealthColor(double h) {
            h = Math.Clamp(h, 0, 1);
            int r, g;
            if(h >= 0.5) { r = (int)Math.Round((1 - h) * 2 * 220); g = 200; }
            else { r = 220; g = (int)Math.Round(h * 2 * 200); }
            return new Color(Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), 0);
        }

        private static string HealthDot(double h) => h >= 0.8 ? "\U0001F7E2" : h >= 0.5 ? "\U0001F7E1" : "\U0001F534";
        private static int HealthPct(double h) => (int)Math.Round(Math.Clamp(h, 0, 1) * 100);

        private sealed record SysLoadSnapshot(
            long Ping, double WorkingMb, double GcHeapMb, int Threads, double CpuMin, int CacheCount,
            int Tracked, int Pending, int ActiveCoops, int DbUsers, int Contracts, int Events, int AutoLogs,
            long ApiCalls, long ApiFails, long DbQueries, long Commands, long CmdFails, long DiscordOps,
            int Latency, int Guilds, int QHigh, int QLow, int QHighW, int QLowW,
            double RuntimeHealth, double DiscordHealth, double ProcessHealth, double DbHealth,
            long StartedUnix, long NowUnix) {
            public double Worst => Math.Min(Math.Min(RuntimeHealth, DiscordHealth), Math.Min(ProcessHealth, DbHealth));
        }

        private static async Task<SysLoadSnapshot> GatherSysLoad(ApplicationDbContext db, DiscordSocketClient client, IDiscordQueue queue) {
            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var proc = Process.GetCurrentProcess();
            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;
            var cacheCount = db._cache is MemoryCache mc ? mc.Count : -1;
            var pending = db.ChangeTracker.Entries().Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
            var tracked = db.ChangeTracker.Entries().Count();

            var activeCoops = await db.Coops.CountAsync(x => !x.Finished && x.CoopEnds > DateTimeOffset.UtcNow);
            var dbUsers = await db.DBUsers.CountAsync();
            var contracts = await db.Contracts.CountAsync();
            var events = await db.Events.CountAsync();
            var autoLogs = await db.AutomationLogs.CountAsync(x => x.StartTime > DateTimeOffset.UtcNow.AddDays(-1));

            var latency = client?.Latency ?? -1;
            var guilds = client?.Guilds?.Count ?? 0;
            var qHigh = queue?.HighDepth ?? 0;
            var qLow = queue?.LowDepth ?? 0;
            var backlog = qHigh + qLow;

            var apiCalls = RuntimeMetrics.ApiCalls;
            var apiFails = RuntimeMetrics.ApiFailures;
            var commands = RuntimeMetrics.Commands;
            var cmdFails = RuntimeMetrics.CommandFailures;

            var runtimeHealth = Math.Min(apiCalls == 0 ? 1 : 1 - (double)apiFails / apiCalls, commands == 0 ? 1 : 1 - (double)cmdFails / commands);
            var discordHealth = Math.Min(latency < 0 ? 1 : HealthRange(latency, 150, 1000), HealthRange(backlog, 25, 500));
            var processHealth = Math.Min(HealthRange(workingMb, 1200, 4000), HealthRange(gcHeapMb, 500, 3000));
            var dbHealth = Math.Min(HealthRange(pingMs, 50, 500), HealthRange(pending, 25, 250));

            return new SysLoadSnapshot(pingMs, workingMb, gcHeapMb, proc.Threads.Count, proc.TotalProcessorTime.TotalMinutes, cacheCount,
                tracked, pending, activeCoops, dbUsers, contracts, events, autoLogs,
                apiCalls, apiFails, RuntimeMetrics.DbQueries, commands, cmdFails, RuntimeMetrics.DiscordOps,
                latency, guilds, qHigh, qLow, queue?.HighWorkers ?? 0, queue?.LowWorkers ?? 0,
                runtimeHealth, discordHealth, processHealth, dbHealth,
                RuntimeMetrics.StartedAt.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }

        private static string SysLoadContent(SysLoadSnapshot s) =>
            $"-# Counters since <t:{s.StartedUnix}:R> · updated <t:{s.NowUnix}:R>";

        private static Embed SysLoadSection(string section, SysLoadSnapshot s) {
            string Metric(long total, double perMin) => $"`{total:N0}` total\n`{perMin:F1}`/min";

            return section switch {
                "runtime" => new EmbedBuilder()
                    .WithAuthor($"Runtime Usage  —  {HealthPct(s.RuntimeHealth)}% healthy")
                    .WithColor(HealthColor(s.RuntimeHealth))
                    .AddField("Egg Inc API", Metric(s.ApiCalls, RuntimeMetrics.PerMinute(s.ApiCalls)) + (s.ApiFails > 0 ? $"\n`{s.ApiFails:N0}` failed" : ""), inline: true)
                    .AddField("DB Queries", Metric(s.DbQueries, RuntimeMetrics.PerMinute(s.DbQueries)), inline: true)
                    .AddField("Commands", Metric(s.Commands, RuntimeMetrics.PerMinute(s.Commands)) + (s.CmdFails > 0 ? $"\n`{s.CmdFails:N0}` failed" : ""), inline: true)
                    .AddField("Discord Ops", Metric(s.DiscordOps, RuntimeMetrics.PerMinute(s.DiscordOps)), inline: true)
                    .Build(),
                "discord" => new EmbedBuilder()
                    .WithAuthor($"Discord  —  {HealthPct(s.DiscordHealth)}% healthy")
                    .WithColor(HealthColor(s.DiscordHealth))
                    .AddField("Gateway", $"`{s.Latency}` ms", inline: true)
                    .AddField("Guilds", $"`{s.Guilds}`", inline: true)
                    .AddField("Send Queue", $"H `{s.QHigh}` / `{s.QHighW}`w\nL `{s.QLow}` / `{s.QLowW}`w", inline: true)
                    .Build(),
                "process" => new EmbedBuilder()
                    .WithAuthor($"Process  —  {HealthPct(s.ProcessHealth)}% healthy")
                    .WithColor(HealthColor(s.ProcessHealth))
                    .AddField("Uptime", $"`{FormatUptime(RuntimeMetrics.Uptime)}`", inline: true)
                    .AddField("Working Set", $"`{s.WorkingMb:F1}` MB", inline: true)
                    .AddField("GC Heap", $"`{s.GcHeapMb:F1}` MB", inline: true)
                    .AddField("GC 0/1/2", $"`{GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}`", inline: true)
                    .AddField("Threads", $"`{s.Threads}`", inline: true)
                    .AddField("CPU Time", $"`{s.CpuMin:F1}` min", inline: true)
                    .Build(),
                "database" => new EmbedBuilder()
                    .WithAuthor($"Database  —  {HealthPct(s.DbHealth)}% healthy")
                    .WithColor(HealthColor(s.DbHealth))
                    .AddField("DB Ping", $"`{s.Ping}` ms", inline: true)
                    .AddField("Tracked", $"`{s.Tracked}`", inline: true)
                    .AddField("Pending", $"`{s.Pending}`", inline: true)
                    .AddField("Mem Cache", s.CacheCount >= 0 ? $"`{s.CacheCount}`" : "`n/a`", inline: true)
                    .AddField("DBUsers", $"`{s.DbUsers:N0}`", inline: true)
                    .AddField("Active Coops", $"`{s.ActiveCoops:N0}`", inline: true)
                    .AddField("Contracts", $"`{s.Contracts:N0}`", inline: true)
                    .AddField("Events", $"`{s.Events:N0}`", inline: true)
                    .AddField("AutoLogs 24h", $"`{s.AutoLogs:N0}`", inline: true)
                    .Build(),
                _ => new EmbedBuilder()
                    .WithAuthor($"System Load  —  {HealthPct(s.Worst)}% healthy")
                    .WithColor(HealthColor(s.Worst))
                    .WithDescription("Pick a section below for details.")
                    .AddField($"{HealthDot(s.RuntimeHealth)} Runtime", $"{HealthPct(s.RuntimeHealth)}%\n`{RuntimeMetrics.PerMinute(s.ApiCalls):F1}` API/min", inline: true)
                    .AddField($"{HealthDot(s.DiscordHealth)} Discord", $"{HealthPct(s.DiscordHealth)}%\n`{s.Latency}` ms, `{s.QHigh + s.QLow}` queued", inline: true)
                    .AddField($"{HealthDot(s.ProcessHealth)} Process", $"{HealthPct(s.ProcessHealth)}%\n`{s.WorkingMb:F0}` MB", inline: true)
                    .AddField($"{HealthDot(s.DbHealth)} Database", $"{HealthPct(s.DbHealth)}%\n`{s.Ping}` ms ping", inline: true)
                    .Build()
            };
        }

        private static bool IsEphemeral(IMessage m) => m?.Flags?.HasFlag(MessageFlags.Ephemeral) ?? false;

        private static MessageComponent SysLoadComponents(string section, bool autoRefreshing, bool ephemeral) {
            var menu = new SelectMenuBuilder()
                .WithCustomId("SysLoadNav")
                .WithPlaceholder("View section...")
                .AddOption("Overview", "overview", isDefault: section == "overview")
                .AddOption("Runtime Usage", "runtime", isDefault: section == "runtime")
                .AddOption("Discord", "discord", isDefault: section == "discord")
                .AddOption("Process", "process", isDefault: section == "process")
                .AddOption("Database", "database", isDefault: section == "database");
            var cb = new ComponentBuilder().WithSelectMenu(menu);
            // While auto-refreshing: Stop. Otherwise: a manual Refresh (carries the
            // current section so it can re-render it). Dismiss only for in-channel
            // (non-ephemeral) messages, since ephemeral ones can be dismissed natively.
            if(autoRefreshing)
                cb.WithButton("Stop refreshing", customId: "SysLoadStop", style: ButtonStyle.Secondary, row: 1);
            else
                cb.WithButton("Refresh", customId: $"SysLoadRefresh:{section}", style: ButtonStyle.Primary, row: 1);
            if(!ephemeral)
                cb.WithButton("Dismiss", customId: "SysLoadDismiss", style: ButtonStyle.Danger, row: 1);
            return cb.Build();
        }

        [SlashCommand(Description = "System load: runtime, Discord, DB, process (health-colored)", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "a")]
        public static async Task SysLoad(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, IServiceProvider serviceProvider,
            [SlashParam(Required = false, Description = "Auto-refresh every N seconds (1-30, stops after 30s total)")] int refreshseconds = 0,
            [SlashParam(Required = false, Description = "Post visibly in the channel instead of only to you")] bool showinchannel = false) {

            await command.DeferAsync(ephemeral: !showinchannel);

            var queue = serviceProvider.GetService<IDiscordQueue>();
            var snap = await GatherSysLoad(db, client, queue);
            var refreshing = refreshseconds > 0;
            var interval = Math.Clamp(refreshseconds, 1, 30);

            var message = await command.RespondAsyncGettingMessage(content: SysLoadContent(snap), embed: SysLoadSection("overview", snap),
                ephemeral: !showinchannel, components: SysLoadComponents("overview", refreshing, !showinchannel));
            if(!refreshing || message is null)
                return;

            var cts = new CancellationTokenSource();
            var session = new SysLoadSession { Cts = cts, Section = "overview" };
            _sysLoad[message.Id] = session;
            var factory = serviceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();

            // Re-render the currently-shown section until Stop, deletion, or 30s cap.
            _ = Task.Run(async () => {
                var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
                try {
                    while(!cts.IsCancellationRequested && DateTimeOffset.UtcNow < deadline) {
                        await Task.Delay(TimeSpan.FromSeconds(interval), cts.Token);
                        if(cts.IsCancellationRequested)
                            break;
                        using var tickDb = await factory.CreateDbContextAsync();
                        var fresh = await GatherSysLoad(tickDb, client, queue);
                        var sec = session.Section;
                        await command.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(fresh); x.Embed = SysLoadSection(sec, fresh); x.Components = SysLoadComponents(sec, true, !showinchannel); });
                    }
                } catch(OperationCanceledException) {
                } catch(Exception) {
                    // message gone / edit failed - stop quietly
                } finally {
                    _sysLoad.TryRemove(message.Id, out _);
                    // Auto-refresh ended: swap Stop -> manual Refresh (+ Dismiss if in-channel).
                    try { await command.ModifyOriginalResponseAsync(x => x.Components = SysLoadComponents(session.Section, false, !showinchannel)); } catch { }
                }
            }, cts.Token);
        }

        [ComponentCommand]
        public static async Task SysLoadNav(SocketMessageComponent component, ApplicationDbContext db, DiscordSocketClient client, IServiceProvider serviceProvider) {
            await component.DeferAsync();
            var section = component.Data.Values.FirstOrDefault() ?? "overview";
            var refreshing = _sysLoad.TryGetValue(component.Message.Id, out var session);
            if(refreshing)
                session.Section = section;

            var snap = await GatherSysLoad(db, client, serviceProvider.GetService<IDiscordQueue>());
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentCommand]
        public static async Task SysLoadRefresh(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db, DiscordSocketClient client, IServiceProvider serviceProvider) {
            await component.DeferAsync();
            var section = string.IsNullOrEmpty(data) ? "overview" : data;
            var refreshing = _sysLoad.ContainsKey(component.Message.Id);
            var snap = await GatherSysLoad(db, client, serviceProvider.GetService<IDiscordQueue>());
            await component.ModifyOriginalResponseAsync(x => { x.Content = SysLoadContent(snap); x.Embed = SysLoadSection(section, snap); x.Components = SysLoadComponents(section, refreshing, IsEphemeral(component.Message)); });
        }

        [ComponentCommand]
        public static async Task SysLoadStop(SocketMessageComponent component) {
            await component.DeferAsync();
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                session.Cts.Cancel();
        }

        [ComponentCommand]
        public static async Task SysLoadDismiss(SocketMessageComponent component) {
            if(_sysLoad.TryGetValue(component.Message.Id, out var session))
                session.Cts.Cancel();
            try {
                await component.Message.DeleteAsync();
            } catch {
                // already gone - acknowledge so the interaction does not error
                try { await component.DeferAsync(); } catch { }
            }
        }

        [SlashCommand(Description = "One-look bot/DB/deploy/load status", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task BotStatus(FauxCommand command, ApplicationDbContext db, IServiceProvider serviceProvider, CoopStatsCache stats) {
            await command.DeferAsync(ephemeral: true);

#if DEBUG
            var buildConfig = "Debug";
#elif DEV9001
            var buildConfig = "DEV9001";
#elif DEV9002
            var buildConfig = "DEV9002";
#elif RELEASE
            var buildConfig = "Release";
#else
            var buildConfig = "Unknown";
#endif

            var botActive = Environment.GetEnvironmentVariable("BOT_ACTIVE") ?? "(unset)";
            var botColor = Environment.GetEnvironmentVariable("BOT_COLOR") ?? "(unset)";
            var proc = Process.GetCurrentProcess();
            var uptime = (DateTime.Now - proc.StartTime).Humanize();

            var updaters = serviceProvider.GetServices<IHostedService>().OfType<IUpdaterService>().ToList();
            var runningServices = updaters.Count(x => x.Running());

            var sw = Stopwatch.StartNew();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
            var pingMs = sw.ElapsedMilliseconds;

            var trackerEntries = db.ChangeTracker.Entries().ToList();
            var pending = trackerEntries.Count(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

            var workingMb = proc.WorkingSet64 / 1_048_576.0;
            var gcHeapMb = GC.GetTotalMemory(false) / 1_048_576.0;
            var cacheCount = db._cache is MemoryCache mc ? mc.Count : -1;

            var statsAge = stats.LastRefresh is { } t ? (DateTimeOffset.UtcNow - t).Humanize().ShortenTime() : "never";
            var server = command.GuildId.HasValue ? stats.GetServerStats(command.GuildId.Value) : null;

            var rows = new List<List<FixedWidthCell>> {
                new() { new("Bot Active"), new(botActive, CellAlignment.Right) },
                new() { new("Bot Color"), new(botColor, CellAlignment.Right) },
                new() { new("Build"), new(buildConfig, CellAlignment.Right) },
                new() { new("Uptime"), new(uptime, CellAlignment.Right) },
                null,
                new() { new("Services Up"), new($"{runningServices}/{updaters.Count}", CellAlignment.Right) },
                null,
                new() { new("DB Ping"), new($"{pingMs} ms", CellAlignment.Right) },
                new() { new("Tracked"), new($"{trackerEntries.Count}", CellAlignment.Right) },
                new() { new("Pending"), new($"{pending}", CellAlignment.Right) },
                null,
                new() { new("Working Set"), new($"{workingMb:F1} MB", CellAlignment.Right) },
                new() { new("GC Heap"), new($"{gcHeapMb:F1} MB", CellAlignment.Right) },
                new() { new("GC (0/1/2)"), new($"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}", CellAlignment.Right) },
                new() { new("Cache"), new(cacheCount >= 0 ? $"{cacheCount}" : "n/a", CellAlignment.Right) },
                new() { new("Stats Age"), new(statsAge, CellAlignment.Right) },
                null,
                new() { new("Active Contracts"), new($"{server?.ActiveContracts ?? 0}", CellAlignment.Right) },
                new() { new("Active Coops"), new($"{server?.ActiveCoops ?? 0}", CellAlignment.Right) },
                new() { new("Pending Threads"), new($"{server?.PendingThreads ?? 0}", CellAlignment.Right) },
                new() { new("Players In Coops"), new($"{server?.UsersAssigned ?? 0}", CellAlignment.Right) },
            };

            await command.RespondAsync($"```\n{GetTable(rows)}```", ephemeral: true);
        }

        [SlashCommand(Description = "Active Co-op Stats", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task CoopStats(FauxCommand command, ApplicationDbContext db) {
            await command.DeferAsync();
            var coops = await db.Coops.Where(x => !x.Finished && x.Status != CoopStatusEnum.Failed && x.GuildId == command.GuildId && !x.DeletedChannel && x.CoopEnds > DateTimeOffset.UtcNow).ToListAsync();
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

            await command.RespondAsync(messages.First());
            for(var i = 1; i < messages.Count; i++) {
                await command.Channel.SendMessageAsync(messages[i]);
            }
        }

        [SlashCommand(Description = "Disable user, user will not be assigned to co-ops until re-enabled", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task Disable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketUser user) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = true;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is disabled.");
        }

        [SlashCommand(Description = "Re-enable user", AdminOnly = StaffOnlyLevel.Admin)]
        public static async Task Enable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketUser user) {
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"));
                return;
            }

            dbuser.TempDisabled = false;
            await db.SaveChangesAsync();

            var responseText = (dbuser.NextBreakExpire is not null && dbuser.NextBreakExpire > DateTimeOffset.UtcNow) ? $" when their break expires {DiscordHelpers.TimeStamper((DateTimeOffset)dbuser.NextBreakExpire, DiscordHelpers.DiscordTimestampFormat.Relative)}" : " from now on.";

            await command.RespondAsync($"{user.Mention} is enabled and will be assigned to co-ops {responseText}");
        }
    }
}

