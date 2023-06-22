using Cronos;

using Discord.WebSocket;

using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;

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
                try {
                    foreach(var account in user.EggIncAccounts) {
                        var discorduser = _discord.GetUser(user.DiscordId);
                        if(discorduser is null) {
                            continue;
                        }
                        var dmChannel = await discorduser.CreateDMChannelAsync();
                        if(dmChannel is null) {
                            _logger.LogWarning($"Could not create DM channel with {user.DiscordUsername}");
                            continue;
                        }

                        if(!account.SentBreakWarning) {

                            _logger.LogInformation($"Sending warning to {user.DiscordUsername}");
                            var nextContract = CronExpression.Parse("0 11 * * MON,WED,FRI").GetNextOccurrence(account.OnBreakUntil, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time"));

                            await dmChannel.SendMessageAsync($"Your break for {account.Name} is expiring {DiscordHelpers.TimeStamper(account.OnBreakUntil, DiscordHelpers.DiscordTimestampFormat.Relative)}.\n\nPlease use the `/mycontractsettings` command to extend your break if you need more time, otherwise you will be assigned a co-op for the next contract on {DiscordHelpers.TimeStamper(nextContract.Value, DiscordHelpers.DiscordTimestampFormat.LongDateWShortTime)}.");
                            account.BreakWarningSent(user);
                        }
                    }
                } catch(Exception e) {
                    _logger.LogError(e, $"Error sending warning to {user.DiscordUsername}");
                    _bugsnag.Notify(e);
                }
            }
            if(users.Count > 0) {
                await _db.SaveChangesAsync();
            }
        }
    }
}
