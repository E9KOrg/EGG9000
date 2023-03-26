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
using EGG9000.Bot.Services;
using RazorEngine.Compilation.ImpromptuInterface.Dynamic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;

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
        public static async Task Status(FauxCommand command, ApplicationDbContext db) {
            var lastComplete = await db.AutomationLogs.Where(x => x.EndTime.HasValue).GroupBy(x => x.Type).Select(x => x.OrderByDescending(y => y.EndTime).First()).ToListAsync();
            var message = new StringBuilder();
            foreach(var log in lastComplete.OrderBy(x => x.Type)) {
                var incompletes = await db.AutomationLogs.Where(x => x.StartTime > log.EndTime && x.Type == log.Type).ToListAsync();
                message.Append($"**{log.Type}** Last finished {(DateTimeOffset.Now - log.EndTime.Value).Humanize()}");
                if(incompletes.Any(x => x.Skipped)) {
                    message.Append($" attempted to run {incompletes.Count} more times");
                }
                if(incompletes.Any(x => !x.Skipped)) {
                    message.Append($" latest run has been going for {(DateTimeOffset.Now - incompletes.Last(x => !x.Skipped).StartTime).Humanize()}");
                }
                message.Append("\n");
            }

            await command.RespondAsync(message.ToString());
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
                await (service as IUpdaterBase).ForceStopAsync();
            } catch(Exception e) {
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await command.ModifyOriginalResponseAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {serviceName}");
            }
            await command.ModifyOriginalResponseAsync($"Restarted {serviceName}");
        }
    }
}

