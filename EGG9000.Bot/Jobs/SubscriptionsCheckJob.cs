using Cronos;

using Discord.WebSocket;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Jobs {

    public class SubscriptionsCheckJob {
        private readonly ILogger<SubscriptionsCheckJob> _logger;
        private readonly DiscordSocketClient _discord;
        private readonly Bugsnag.IClient _bugsnag;
        private readonly IDbContextFactory<ApplicationDbContext> _dbFactory;


        public SubscriptionsCheckJob(ILogger<SubscriptionsCheckJob> logger, DiscordSocketClient discord, Bugsnag.IClient bugsnag, IDbContextFactory<ApplicationDbContext> dbFactory) {
            _logger = logger;
            _discord = discord;
            _bugsnag = bugsnag;
            _dbFactory = dbFactory;
        }


#if DEBUG
        //[Job("0 0 */1 * * *")]
        [Job("0 * * * * *")]
#else
        [Job("0 0 */1 * * *")]
#endif
        public async Task CheckSubscriptions() {
            _logger.LogInformation("Checking subscriptions");
            var db = await _dbFactory.CreateDbContextAsync(); ;
#if DEBUG
            var users = db.DBUsers.Where(x => !x.TempDisabled && x.GuildId > 0).ToList();
            //var users = db.DBUsers.Where(x => x.DiscordId == 899062018194161695).ToList();
            //users = users.Where(x => x.DiscordId == 899062018194161695).ToList();
#else
            var users = db.DBUsers.Where(x => !x.TempDisabled && x.GuildId > 0).ToList();
#endif
            foreach(var guildGroup in users.GroupBy(x => x.GuildId)) {
                var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == guildGroup.Key);
                if(dbguild is null)
                    continue;
                var guild = _discord.GetGuild(guildGroup.Key);
                var standardRoleId = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.StandardSubscription)?.Id;
                var proRoleId = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.ProSubscription)?.Id;

                await Parallel.ForEachAsync(guildGroup, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (user, cancellationToken) => {
                    try {
                        foreach(var account in user.EggIncAccounts) {
                            await CheckSubscription(db, _discord, user, account, dbguild, guild);
                            //var subscriptionStatus = await ContractsAPI.GetUserSubscription(account.Id);
                            //if(subscriptionStatus.HasStatus && subscriptionStatus.Status == Ei.UserSubscriptionInfo.Types.Status.Active || (subscriptionStatus.Status == Ei.UserSubscriptionInfo.Types.Status.GracePeriod && subscriptionStatus.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                            //    if(account.SubscriptionLevel != subscriptionStatus.SubscriptionLevel) {
                            //        await SendUltraLogMessage(user, account, (int)subscriptionStatus.SubscriptionLevel, (int)account.SubscriptionLevel, dbguild, guild);
                            //        account.SubscriptionLevel = subscriptionStatus.SubscriptionLevel;
                            //        user.UpdateAccounts();
                            //    }
                            //    if(account.SubscriptionEnds != subscriptionStatus.PeriodEnd) {
                            //        account.SubscriptionEnds = subscriptionStatus.PeriodEnd;
                            //        user.UpdateAccounts();
                            //    }
                            //} else {
                            //    if(account.SubscriptionLevel.HasValue) {
                            //        await SendUltraLogMessage(user, account, (int)account.SubscriptionLevel, 0, dbguild, guild);
                            //        account.SubscriptionLevel = null;
                            //        user.UpdateAccounts();
                            //    }
                            //}
                        }
                        var discorduser = guild.GetUser(user.DiscordId);
                        if(discorduser is not null) {
                            await CheckRole(standardRoleId, user, false, discorduser);
                            await CheckRole(proRoleId, user, true, discorduser);
                        }
                    } catch(Exception e) {
                        _bugsnag.Notify(e);
                    }
                });
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Finished checking subscriptions");
        }

        private async Task CheckSubscription(ApplicationDbContext db, DiscordSocketClient _client, DBUser user, EggIncAccount account, Guild dbGuild, SocketGuild guild) {
            try {
                var subscriptionStatus = await ContractsAPI.GetUserSubscription(account.Id);
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
            var response = await ChannelHelper.DetermineAndSend(db, _client, dbGuild, guild, GuildChannelType.UltraLog, new() { Text = message});
        }

        public async Task CheckRole(ulong? roleid, DBUser dbuser, bool pro, SocketGuildUser user) {
            if(roleid is null)
                return;
            var needsRole = dbuser.EggIncAccounts.Any(y => y.SubscriptionLevel == (pro ? Ei.UserSubscriptionInfo.Types.Level.Pro : Ei.UserSubscriptionInfo.Types.Level.Standard) && y.HasActiveSubscription());
            var hasRole = user.Roles.Any(x => x.Id == roleid);

            if(hasRole && !needsRole) {
                await user.RemoveRoleAsync(roleid.Value);
                _logger.LogInformation("Removed {level} subscription role from {user}", pro ? "pro" : "standard", user.GetCleanName());
            } else if(!hasRole && needsRole) {
                await user.AddRoleAsync(roleid.Value);
                _logger.LogInformation("Added {level} subscription role to {user}", pro ? "pro" : "standard", user.GetCleanName());
            }
        }
    }
}
