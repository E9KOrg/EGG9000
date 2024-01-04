using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using static EGG9000.Bot.Commands.ContractCommandsSlash;
using EGG9000.Common.Services;
using RazorEngine.Compilation.ImpromptuInterface.Dynamic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using System.Collections;
using System.Numerics;
using EGG9000.Bot.Services;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;

namespace EGG9000.Bot.Commands {
    public static class StaffCommands {
        [SlashCommand(Description = "Mark a potential artifact cheater as clean", AdminOnly = StaffOnlyLevel.CluckingCoordinator, ParentCommand = "a")]
        public static async Task MarkAFSClean(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount) {
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
                await command.RespondAsync($"User account `<@{dbuser.DiscordId}>` ({identifier}) marked as having clean artifacts.");
#else
                await command.RespondAsync($"User account` <@{dbuser.DiscordId}>` ({identifier}) marked as having clean artifacts.");
#endif
                account.AFSMarkedClean = true;
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();
            }
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
        public static async Task AS(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string message, [SlashParam(Required = false)] SocketChannel channel = null, [SlashParam(Required = false, Description = "Message ID to reply to")] string replyto = null) {
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
        public static async Task SelectRoleUsers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(Required = true)] int numberOfUsers, [SlashParam(Required = true)] SocketRole role, [SlashParam(Required = false)] SocketRole role2, [SlashParam(Required = false)] SocketRole role3) {
            try {
                var guildUsers = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId).Users;
                var usersWithRole = guildUsers.Where(u => u.Roles.Contains(role));
                if(role2 is not null) usersWithRole = usersWithRole.Where(u => u.Roles.Contains(role2));
                if(role3 is not null) usersWithRole = usersWithRole.Where(u => u.Roles.Contains(role3));
                var rnd = new Random();
                var randomUsers = usersWithRole.OrderBy(u => rnd.Next()).Take(numberOfUsers);

#if DEBUG || DEV9002
                var userList = string.Join("\n", randomUsers.Select(u => $"{u.Username} ({u.Id})"));
#else
                var userList = string.Join("\n", randomUsers.Select(u => $"<@{u.Id}>"));
#endif

                await command.RespondAsync(userList);
            } catch(Exception ex) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to parse role `{role}`.\n\n**Message**\n{ex.Message}"));
                return;
            }
        }

        [SlashCommand(Description = "Look for a coop with certain search parameters", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task LocateCoop(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = true)] string coopname = "", [SlashParam(Required = false)] SocketChannel contractchannel = null) {
            //Coop name was not passed correctly, error out
            if(string.IsNullOrEmpty(coopname) || string.IsNullOrWhiteSpace(coopname)) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to parse the coop name `{coopname}`. Check you've entered a value?"), ephemeral: true);
                return;
            } else coopname = coopname.ToLower(); //To-lower it

            //Define a condition for lessening the DB load
            Func<Coop, bool> rightContract = coop => {
                if(contractchannel is null) return true;
                else if(coop?.Contract is null) return false;
                else return (coop.Contract.ID == contractchannel.ToString());
            };


            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == command.GuildId || x.OverflowServersJson.Contains(command.GuildId.ToString()));

            //Attempt to find the coop
            var findCoop = await db.Coops.Include(x => x.Contract).Include(x => x.UserCoopsXrefs).ThenInclude(u => u.User).AsQueryable().FirstOrDefaultAsync(x => x.GuildId == guild.Id && x.Name.ToLower() == coopname);

            //If it can't be found, error out
            if(findCoop is null) {
                await command.RespondAsync(content: "", embed: EmbedError($"Unable to find a coop named `{coopname + (contractchannel is null ? "" : $"for the contract {contractchannel}")}`"), ephemeral: true);
                return;
            }

            var builder = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder()
                    .WithName($"{findCoop.Contract.Name} - {findCoop.Name}")
                    .WithIconUrl(EggIncEggs.GetEggById((int)findCoop.Contract.Details.Egg).Image)
                    .WithUrl($"https://egg9000.com/coop/{findCoop.Contract.ID}/{findCoop.Name.ToLower()}"))
                .WithColor((!findCoop.Finished && findCoop.FinishedOrFailed()) ? Color.Red : (findCoop.ProjectedToFinish ? Color.Green : Color.Blue));

            //For each item in coopName.UserCoopsXrefs, append a user mention to a variable
            var userList = new List<string> {
                $"**__Coop Users {findCoop?.UserCoopsXrefs.Count(u => u.JoinedCoop) ?? 0}/{findCoop?.UserCoopsXrefs.Count ?? 0}__:**"
            };
            userList.AddRange(findCoop?.UserCoopsXrefs?.Select(u => $"{(u.JoinedCoop ? "✓" : "❌")} <@{u.User.DiscordId}>").ToList());
            if(userList.Count == 1) {
                userList.Add("..._No users_");
            } else if(userList.Count > 10) {
                userList = userList.Take(10).ToList();
                userList.Add("..._(Trimmed list)_");
            }

            builder.WithDescription(string.Join("\n", userList));

            builder.AddField("Channel", $"{(findCoop.DeletedChannel ? "**Channel Deleted**" : "<#" + findCoop.DiscordChannelId + ">")}");
            builder.AddField("League", (findCoop.AnyLeague ? "<:ultra:1131045418319495369> Any Grade" : "") + PlayerGradeDetails.GetEmoji(findCoop.League));

            if(findCoop.Finished) builder.AddField("Status", "**Finished**");
            else if(findCoop.FinishedOrFailed()) builder.AddField("Status", "**Failed**");
            else builder.AddField("Projected to finish?", $"{(findCoop.ProjectedToFinish ? "Yes" : "No")}");

            await command.RespondAsync("", embed: builder.Build(), ephemeral: true);
        }

        [SlashCommand(Description = "Add a temporary prefex for a users co-op (PrefixWord11)", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task TemporaryPrefix(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketGuildUser user, [SlashParam] string prefix, [SlashParam] string timespan) {
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
                await command.ModifyOriginalResponseAsync(x => { x.Content = $""; x.Embed = EmbedError("Unable to find user"); });
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
            var table = new List<List<FixedWidthCell>> {
                new List<FixedWidthCell> {
                    new FixedWidthCell ("Name"),
                    new FixedWidthCell ("Avg"),
                    new FixedWidthCell ("Last🏁"),
                    new FixedWidthCell("Attempts"),
                    new FixedWidthCell("Status")
                }
            };
            foreach(var log in lastComplete.OrderBy(x => x.Type)) {
                var incompletes = await db.AutomationLogs.Where(x => x.StartTime > log.EndTime && x.Type == log.Type).ToListAsync();
                var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == log.Type);
                table.Add(new List<FixedWidthCell> {
                    new FixedWidthCell (log.Type),
                    new FixedWidthCell (
                        averages.Any(x => x.Type == log.Type) ?
                        TimeSpan.FromSeconds(averages.First(x => x.Type == log.Type).Avg).Humanize().ShortenTime()
                        : ""),
                    new FixedWidthCell ((DateTimeOffset.Now - log.EndTime.Value).Humanize().ShortenTime()),
                    new FixedWidthCell (incompletes.Count.ToString()),
                    new FixedWidthCell (
                        (service as IUpdaterService).Running() ?
                            (incompletes.Any(x => !x.Skipped) ?
                            $"Current run {(DateTimeOffset.Now - incompletes.Last(x => !x.Skipped).StartTime).Humanize().ShortenTime()}"
                            : "Started"
                            )
                        : "Stopped"
                        )
                });
            }

            await command.RespondAsync($"```\n{GetTable(table)}```");
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task RestartService(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsync($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);

                return;
            }

            await command.RespondAsync($"Attempting to restart {serviceName}");
            try {
                await service.StopAsync(new System.Threading.CancellationToken());
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
            await command.ModifyOriginalResponseAsync($"Restarted {serviceName}");
        }

        [SlashCommand(Description = "Stop an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StopService(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsync($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);
                
                return;
            }
            if(!(service as IUpdaterService).Running()) {
                await command.RespondAsync($"The service {serviceName} is already stopped.");
                return;
            }
            await command.RespondAsync($"Attempting to stop {serviceName}");
            try {
                await service.StopAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
            await command.ModifyOriginalResponseAsync($"Stopped {serviceName}");
        }

        [SlashCommand(Description = "Run automated service now", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task RunService(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsync($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);
            }
            if(!(service as IUpdaterService).Running()) {
                await command.RespondAsync($"The service {serviceName} is already stopped.");
                return;
            }
            await command.RespondAsync($"Attempting to run {serviceName}");
            try {
                (service as IUpdaterService).ResetTimer();
            } catch(Exception e) {
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task StartService(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(ServiceNameAutoComplete))] string serviceName, IServiceProvider serviceProvider, JobService jobService) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                var job = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                          .SelectMany(t => t.GetMethods())
                          .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                          .FirstOrDefault(x => x.Name == serviceName);
                if(job is null) {
                    await command.RespondAsync($"Unable to locate a service/job with the name {serviceName}");
                    return;
                }

                jobService.RunJob(serviceName);
            }
            if((service as IUpdaterService).Running()) {
                await command.RespondAsync($"The service {serviceName} is already running.");
                return;
            }
            await command.RespondAsync($"Attempting to start {serviceName}");
            try {
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                var frame = new StackTrace(e, true).GetFrame(0);
                await command.RespondAsync(content: "", embed: EmbedInternalError($"**Message**:\n{e.Message}\n\n**Frame info**:\n\tFile: {Path.GetFileName(frame.GetFileName() ?? "") ?? "(Unknown)"}\n\tLine: {frame.GetFileLineNumber()}"));
            }
            await command.ModifyOriginalResponseAsync($"Started {serviceName}");
        }
    }
}

