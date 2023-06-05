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
using EGG9000.Common.Services;
using RazorEngine.Compilation.ImpromptuInterface.Dynamic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using EGG9000.Common.Commands;

namespace EGG9000.Bot.Commands {
    public static class StaffCommands {

        [SlashCommand(Description = "Log a Message", AdminOnly = true, AllowFarmHand = true)]
        public static async Task AS(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string message, [SlashParam(Required = false)] SocketChannel channel = null) {
            if(channel == null) {
                await command.Channel.SendMessageAsync(message);
            } else {
                await ((SocketTextChannel)channel).SendMessageAsync(message);
            }
            await command.RespondAsync("Sent", ephemeral: true);
        }

        [SlashCommand(Description = "Select X random users with Y role", AdminOnly = true)]
        public static async Task SelectRoleUsers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(Required=true)] SocketRole role, [SlashParam] int numberOfUsers = 1) {
            try {
                var guildUsers = client.Guilds.FirstOrDefault(g => g.Id == command.GuildId).Users;
                var usersWithRole = guildUsers.Where(u => u.Roles.Contains(role));
                var rnd = new Random();
                var randomUsers = usersWithRole.OrderBy(u => rnd.Next()).Take(numberOfUsers);

#if DEBUG || DEV9002
                var userList = string.Join("\n", randomUsers.Select(u => $"{u.Username} ({u.Id})"));
#else
                var userList = string.Join("\n", randomUsers.Select(u => $"<@{u.Id}>"));

#endif

                await command.RespondAsync(userList);
            } catch(Exception ex){
                await command.RespondAsync($"⚠️ERROR: Unable to parse role `{role}`, {ex.Message}");
                return;
            }
        }

        [SlashCommand(Description = "Add a temporary prefex for a users co-op (PrefixWord11)", AdminOnly = true, AllowFarmHand = true)]
        public static async Task TemporaryPrefix(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user, [SlashParam] string prefix, [SlashParam] string timespan) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"⚠️ERROR: Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }
            await command.DeferAsync();

            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find user");
                return;
            }

            dbuser.CustomCoopName = prefix;
            dbuser.ExpireCustomCoopName = expireTime;
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Added the co-op prefix `{prefix}` to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>. They will have any co-ops they are in named after them during that time.");
        }

        [SlashCommand(Description = "Get the bot's status", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
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
                }) ;
            }

            await command.RespondAsync($"```\n{FixedWidthTable.GetTable(table)}```");
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task RestartService(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Service Name", Required = true)] string serviceName, IServiceProvider serviceProvider) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                await command.RespondAsync($"Unable to locate a service with the name {serviceName}");
                return;
            }
            await command.RespondAsync($"Attempting to restart {serviceName}");
            try {
                await service.StopAsync(new System.Threading.CancellationToken());
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {serviceName}");
            }
            await command.ModifyOriginalResponseAsync($"Restarted {serviceName}");
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task StopService(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Service Name", Required = true)] string serviceName, IServiceProvider serviceProvider) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                await command.RespondAsync($"Unable to locate a service with the name {serviceName}");
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
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {serviceName}");
            }
            await command.ModifyOriginalResponseAsync($"Stopped {serviceName}");
        }

        [SlashCommand(Description = "Run automated service now", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task RunService(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Service Name", Required = true)] string serviceName, IServiceProvider serviceProvider) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                await command.RespondAsync($"Unable to locate a service with the name {serviceName}");
                return;
            }
            if(!(service as IUpdaterService).Running()) {
                await command.RespondAsync($"The service {serviceName} is already stopped.");
                return;
            }
            await command.RespondAsync($"Attempting to run {serviceName}");
            try {
                (service as IUpdaterService).ResetTimer();
            } catch(Exception e) {
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {serviceName}");
            }
        }

        [SlashCommand(Description = "Restart an automated service", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task StartService(FauxCommand command, ApplicationDbContext db, [SlashParam(Description = "Service Name", Required = true)] string serviceName, IServiceProvider serviceProvider) {
            var service = serviceProvider.GetServices<IHostedService>().FirstOrDefault(x => x.GetType().Name == serviceName);

            if(service == null) {
                await command.RespondAsync($"Unable to locate a service with the name {serviceName}");
                return;
            }
            if((service as IUpdaterService).Running()) {
                await command.RespondAsync($"The service {serviceName} is already running.");
                return;
            }
            await command.RespondAsync($"Attempting to start {serviceName}");
            try {
                await service.StartAsync(new System.Threading.CancellationToken());
            } catch(Exception e) {
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {serviceName}");
            }
            await command.ModifyOriginalResponseAsync($"Started {serviceName}");
        }
    }
}

