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
        private readonly ILogger<TestJob> _logger;
        private readonly ApplicationDbContext _db;
        private readonly DiscordSocketClient _discord;
        private readonly Bugsnag.IClient _bugsnag;

        public UserDMsJob(ILogger<TestJob> logger, ApplicationDbContext applicationDbContext, DiscordSocketClient discord, Bugsnag.IClient bugsnag) {
            _logger = logger;
            _db = applicationDbContext;
            _discord = discord;
            _bugsnag = bugsnag;
        }

        [Job("0 */10 * * * *")]
        public async Task WarningBreakExpiring() {
            _logger.LogInformation("Running WarningBreakExpiring");
            var users = _db.DBUsers.Where(x => x.NextBreakExpire != null && x.NextBreakExpire < DateTimeOffset.Now.AddDays(1)).ToList();
            foreach(var user in users) {
                try {
                    foreach(var account in user.EggIncAccounts) {
                        _logger.LogInformation($"Sending warning to {user.DiscordUsername}");
                        var dmChannel = await _discord.GetUser(user.DiscordId).CreateDMChannelAsync();
                        if(dmChannel is null) {
                            _logger.LogError($"Could not create DM channel with {user.DiscordUsername}");
                            continue;
                        }
                        

                        await dmChannel.SendMessageAsync($"Your break for {account.Name} is expiring {DiscordHelpers.TimeStamper(account.OnBreakUntil, DiscordHelpers.DiscordTimestampFormat.Relative)}. Please use the `/mycontractsettings` command to extend your break if you need more time otherwise you will be assigned a co-op for the next contract that comes out after then.");
                        account.BreakWarningSent(user);
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
