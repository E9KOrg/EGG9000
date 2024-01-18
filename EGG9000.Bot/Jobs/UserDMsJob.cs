using Cronos;

using Discord.WebSocket;

using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Humanizer;

using Microsoft.Extensions.Logging;


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Jobs {

    public class UserDMsJob {
        private readonly ILogger<UserDMsJob> _logger;
        private readonly ApplicationDbContext _db;
        private readonly DiscordSocketClient _discord;
        private readonly Bugsnag.IClient _bugsnag;

        public UserDMsJob(ILogger<UserDMsJob> logger, ApplicationDbContext applicationDbContext, DiscordSocketClient discord, Bugsnag.IClient bugsnag) {
            _logger = logger;
            _db = applicationDbContext;
            _discord = discord;
            _bugsnag = bugsnag;
        }

        [Job("0 */30 * * * *")]
        public async Task WarningBreakExpiring() {
            _logger.LogInformation("Running WarningBreakExpiring");
            var users = _db.DBUsers.Where(x => x.NextBreakExpire != null && x.NextBreakExpire < DateTimeOffset.Now.AddDays(1) && x.DiscordId > 0 & x.DiscordId > 0 && x.GuildId > 0).ToList();
            foreach(var user in users) {
                foreach(var account in user.EggIncAccounts) {
                    var discorduser = _discord.GetUser(user.DiscordId);
                    if(discorduser is null) {
                        continue;
                    }
                    var dmChannel = await discorduser.CreateDMChannelAsync();
                    if(dmChannel is null) {
                        _logger.LogWarning("Could not create DM channel with {user}", user.DiscordUsername);
                        continue;
                    }

                    if(!account.SentBreakWarning && account.OnBreakUntil > DateTimeOffset.FromUnixTimeSeconds(0).AddDays(1)) {
                        _logger.LogInformation("Sending warning to {user}", user.DiscordUsername);
                        var nextContract = CronExpression.Parse("0 11 * * MON,WED,FRI").GetNextOccurrence(account.OnBreakUntil, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"));

                        var mcs = (await _discord.GetGlobalApplicationCommandsAsync()).FirstOrDefault(c => c.Type == Discord.ApplicationCommandType.Slash && c.Name == "mycontractsettings");
                        var message = $"Your break for {account.Backup?.UserName ?? "(No Name)"} is expiring {DiscordHelpers.TimeStamper(account.OnBreakUntil, DiscordHelpers.DiscordTimestampFormat.Relative)}." +
                            $"\n\nPlease use the {(mcs is not null ? $"</mycontractsettings:{mcs?.Id ?? 0}>" : "`/mycontractsettings`")} command to extend your break if you need more time, otherwise you will be assigned a co-op for the next contract on " +
                            $"{DiscordHelpers.TimeStamper(nextContract.Value, DiscordHelpers.DiscordTimestampFormat.LongDateWShortTime)}.";
                        var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, message);
                        if((retEx == null) == user.DMSBlocked) {
                            user.DMSBlocked = !user.DMSBlocked;
                            await _db.SaveChangesAsync();
                        }
                        if(retEx != null) _logger.LogError(retEx, "User {user} has DMs blocked", discorduser.Username);

                        account.BreakWarningSent(user);
                    }
                }
            }
            if(users.Count > 0) {
                await _db.SaveChangesAsync();
            }
        }
    }
}
