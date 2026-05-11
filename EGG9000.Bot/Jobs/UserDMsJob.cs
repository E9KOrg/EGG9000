using Discord.WebSocket;

using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;

using Microsoft.Extensions.Logging;


using System;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Bot.Jobs {

    public class UserDMsJob(ILogger<UserDMsJob> logger, ApplicationDbContext applicationDbContext, DiscordSocketClient discord) {
        private readonly ILogger<UserDMsJob> _logger = logger;
        private readonly ApplicationDbContext _db = applicationDbContext;
        private readonly DiscordSocketClient _discord = discord;

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

                    if(!account.SentBreakWarning && account.OnBreakUntil < DateTimeOffset.Now.AddDays(1) && account.OnBreakUntil > DateTimeOffset.Now.AddDays(-1)) {
                        _logger.LogInformation("Sending warning to {user}", user.DiscordUsername);

                        var mcs = (await _discord.GetGlobalApplicationCommandsAsync()).FirstOrDefault(c => c.Type == Discord.ApplicationCommandType.Slash && c.Name == "mycontractsettings");
                        var message = $"Your break for {account.Backup?.UserName ?? "(No Name)"} is expiring {DiscordHelpers.TimeStamper(account.OnBreakUntil, DiscordHelpers.DiscordTimestampFormat.Relative)}." +
                            $"\n\nPlease use the {(mcs is not null ? $"</mycontractsettings:{mcs?.Id ?? 0}>" : "`/mycontractsettings`")} command to extend your break if you need more time, otherwise you will be assigned co-ops after " +
                            $"{DiscordHelpers.TimeStamper(account.OnBreakUntil, DiscordHelpers.DiscordTimestampFormat.LongDateWShortTime)}. (If this time is after a contract release but before the last BG, you would be assigned during a later BG)";
                        var dmResult = await DiscordHelpersExt.BoolSendDm(discorduser, message, _db);
                        if(dmResult != DiscordHelpersExt.DMResult.DiscordError) account.BreakWarningSent(user);
                    }
                }
            }
            if(users.Count > 0) {
                await _db.SaveChangesAsync();
            }
        }
    }
}
