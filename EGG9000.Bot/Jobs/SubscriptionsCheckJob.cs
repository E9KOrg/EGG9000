using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using System.Threading;
using Ei;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;


using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EGG9000.Bot.Jobs {

    public class SubscriptionsCheckJob(ILogger<SubscriptionsCheckJob> logger, DiscordSocketClient discord, Bugsnag.IClient bugsnag, IDbContextFactory<ApplicationDbContext> dbFactory) {
        private readonly ILogger<SubscriptionsCheckJob> _logger = logger;
        private readonly DiscordSocketClient _discord = discord;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory = dbFactory;

#if DEBUG
        //[Job("0 0 */1 * * *")]
        [Job("0 * * * * *")]
#else
        [Job("0 0 */1 * * *")]
#endif
        public async Task CheckSubscriptions() {
            _logger.LogInformation("Checking subscriptions");
            var db = await _dbFactory.CreateDbContextAsync(); ;
            var users = db.DBUsers.Where(x => !x.TempDisabled && x.GuildId > 0).ToList();
            foreach(var guildGroup in users.GroupBy(x => x.GuildId)) {
                var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == guildGroup.Key);
                if(dbguild is null)
                    continue;
                var guild = _discord.GetGuild(guildGroup.Key);
                if(guild is null)
                    continue;
                var standardRoleId = dbguild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.StandardSubscription)?.Id ?? default;
                var proRoleId = dbguild.ChannelDetails?.FirstOrDefault(x => x.ChannelType == GuildChannelType.ProSubscription)?.Id ?? default;

                var tokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                var parallelOptions = new ParallelOptions {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = tokenSource.Token
                };

                await Parallel.ForEachAsync(guildGroup, parallelOptions, async (user, cancellationToken) => {
                    await ProcessUser(db, user, dbguild, guild, standardRoleId, proRoleId, cancellationToken);
                });
            }

            (var success, var retries) = await db.SaveChangesAsyncRetry(retryCount: 2, cancellationToken: CancellationToken.None, _logger);
            if(success)
                _logger.LogInformation("Finished checking subscriptions");
            else
                _logger.LogWarning("Error saving subscription changes after {retry_count}", retries);
        }

        private async Task ProcessUser(ApplicationDbContext db, DBUser user, Guild dbguild, SocketGuild guild, ulong standardRoleId, ulong proRoleId, CancellationToken cancellationToken) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                if(user?.EggIncAccounts?.Count > 0) {
                    foreach(var account in user.EggIncAccounts) {
                        await CheckSubscription(db, _discord, user, account, dbguild, guild);
                    }
                    var discorduser = guild.GetUser(user.DiscordId);
                    if(discorduser is not null) {
                        await CheckRole(standardRoleId, user, false, discorduser);
                        await CheckRole(proRoleId, user, true, discorduser);
                    }
                }
            } catch(Exception e) {
                _bugsnag.Notify(e);
            }
        }

        private async Task CheckSubscription(ApplicationDbContext db, DiscordSocketClient _client, DBUser user, EggIncAccount account, Guild dbGuild, SocketGuild guild) {
            try {
                var subscriptionStatus = await ContractsAPI.GetUserSubscription(account.Id);
                subscriptionStatus ??= await await Task.Delay(250).ContinueWith(async x => await ContractsAPI.GetUserSubscription(account.Id));
                if(subscriptionStatus is null) {
                    _logger.LogWarning("Null response from ContractsAPI.GetUserSubscription for account ID {accountId}", account.Id);
                    return;
                }

                if(subscriptionStatus.HasStatus && (subscriptionStatus.Status == UserSubscriptionInfo.Types.Status.Active || subscriptionStatus.Status == UserSubscriptionInfo.Types.Status.GracePeriod) && subscriptionStatus.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds()) {
                    if(account.SubscriptionLevel != subscriptionStatus.SubscriptionLevel) {
                        await SendUltraLogMessage(db, _client, user, account,(int?)account.SubscriptionLevel ?? -1, (int)subscriptionStatus.SubscriptionLevel, dbGuild, guild);
                        account.SubscriptionLevel = subscriptionStatus.SubscriptionLevel;
                        user.UpdateAccounts();
                    }
                    if(account.SubscriptionEnds != subscriptionStatus.PeriodEnd) {
                        account.SubscriptionEnds = subscriptionStatus.PeriodEnd;
                        user.UpdateAccounts();
                    }
                } else if(account.SubscriptionLevel.HasValue) {
                    await SendUltraLogMessage(db, _client, user, account, (int?)account.SubscriptionLevel ?? -1, -1, dbGuild, guild);
                    account.SubscriptionLevel = null;
                    user.UpdateAccounts();
                } 
            } catch(Exception e) {
                _bugsnag.Notify(e);
            }
        }

        public static string LevelText(int level) {
            return level switch {
                -1 => "Not Subscribed",
                0 => "ULTRA Standard",
                1 => "ULTRA Pro",
                _ => "???"
            };
        }

        public static async Task SendUltraLogMessage(ApplicationDbContext db, DiscordSocketClient _client, DBUser user, EggIncAccount account, int oldLevel, int intNewLevel, Guild dbGuild, SocketGuild guild) {
            var message = $"<@{user.DiscordId}>'s {(user.EggIncAccounts.Count > 1 && (account.Backup.UserName?.Length ?? 0) > 0 ? $" (`{account.Backup.UserName}`) " : "")}ULTRA status changed from `{LevelText(oldLevel)}` to `{LevelText(intNewLevel)}`.";
            _ = await ChannelHelper.DetermineAndSend(db, _client, dbGuild, guild, GuildChannelType.UltraLog, new() { Text = message});
        }

        public async Task CheckRole(ulong roleid, DBUser dbuser, bool pro, SocketGuildUser user) {
            if(roleid == default || dbuser is null || dbuser.EggIncAccounts?.Count == 0|| user is null)
                return;
            var needsRole = dbuser.EggIncAccounts.Any(y => y.SubscriptionLevel == (pro ? UserSubscriptionInfo.Types.Level.Pro : UserSubscriptionInfo.Types.Level.Standard) && y.HasActiveSubscription());
            var hasRole = user?.Roles?.Any(x => x.Id == roleid) ?? false;

            if(hasRole && !needsRole) {
                await user.RemoveRoleAsync(roleid);
                _logger.LogInformation("Removed {level} subscription role from {user}", pro ? "pro" : "standard", user.GetCleanName());
            } else if(!hasRole && needsRole) {
                await user.AddRoleAsync(roleid);
                _logger.LogInformation("Added {level} subscription role to {user}", pro ? "pro" : "standard", user.GetCleanName());
            }
        }
    }
}
