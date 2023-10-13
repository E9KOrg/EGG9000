using Cronos;

using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Humanizer;
using MassTransit.Initializers.TypeConverters;
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
        private readonly IServiceProvider _provider;


        public SubscriptionsCheckJob(ILogger<SubscriptionsCheckJob> logger, DiscordSocketClient discord, Bugsnag.IClient bugsnag, IServiceProvider provider) {
            _logger = logger;
            _discord = discord;
            _bugsnag = bugsnag;
            _provider = provider;
        }


#if DEBUG
        //[Job("0 0 */1 * * *")]
        [Job("0 * * * * *")]
#else
        [Job("0 0 */1 * * *")]
#endif
        public async Task CheckSubscriptions() {
            _logger.LogInformation("Checking subscriptions");
            var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
#if DEBUG
            //var users = db.DBUsers.Where(x => !x.TempDisabled && x.GuildId > 0).ToList();
            var users = db.DBUsers.Where(x => x.DiscordId == 273621777119313921).ToList();
#else
            var users = db.DBUsers.Where(x => !x.TempDisabled && x.GuildId > 0).ToList();
#endif
            foreach(var guildGroup in users.GroupBy(x => x.GuildId)) {
                var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == guildGroup.Key);
                if(dbguild is null)
                    continue;
                var guild = _discord.GetGuild(guildGroup.Key);
                var standardRoleId = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == Common.Database.Entities.GuildChannelType.StandardSubscription)?.Id;
                var proRoleId = dbguild.ChannelDetails.FirstOrDefault(x => x.ChannelType == Common.Database.Entities.GuildChannelType.ProSubscription)?.Id;


                await Parallel.ForEachAsync(guildGroup, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (user, cancellationToken) => {
                    try {
                        foreach(var account in user.EggIncAccounts) {

                            var subscriptionStatus = await ContractsAPI.GetUserSubscription(account.Id);
                            if(subscriptionStatus.HasStatus && subscriptionStatus.Status == Ei.UserSubscriptionInfo.Types.Status.Active || (subscriptionStatus.Status == Ei.UserSubscriptionInfo.Types.Status.GracePeriod && subscriptionStatus.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds())) {
                                if(account.SubscriptionLevel != subscriptionStatus.SubscriptionLevel) {
                                    await SendUltraLogMessage(user, account, (int)subscriptionStatus.SubscriptionLevel, (int)account.SubscriptionLevel, dbguild, guild);
                                    account.SubscriptionLevel = subscriptionStatus.SubscriptionLevel;
                                    user.UpdateAccounts();
                                }
                                if(account.SubscriptionEnds != subscriptionStatus.PeriodEnd) {
                                    account.SubscriptionEnds = subscriptionStatus.PeriodEnd;
                                    user.UpdateAccounts();
                                }
                            } else {
                                if(account.SubscriptionLevel.HasValue) {
                                    await SendUltraLogMessage(user, account, (int)account.SubscriptionLevel, 0, dbguild, guild);
                                    account.SubscriptionLevel = null;
                                    user.UpdateAccounts();
                                }
                            }
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

        public static string LevelText(int level) {
            return level switch {
                0 => "Not Subscribed",
                1 => "ULTRA Standard",
                2 => "ULTRA Pro",
                _ => "???"
            };
        }

        public async Task SendUltraLogMessage(DBUser user, EggIncAccount account, int oldLevel, int intNewLevel, Guild dbGuild, SocketGuild guild) {
            var message = $"<@{user.DiscordId}> (`{account.Name}`)'s ULTRA status changed from `{LevelText(oldLevel)}` to `{LevelText(intNewLevel)}`.";
            var ultraChannelDetails = dbGuild.ChannelDetails.FirstOrDefault(d => d.ChannelType == GuildChannelType.UltraLog);
            if(ultraChannelDetails == null) return;
            var ultraThread = guild.GetThreadChannel(ultraChannelDetails.Id);
            if(ultraThread is not null) {
                await ultraThread.SendMessageAsync(message);
            } else {
                var ultraChannel = guild.GetTextChannel(ultraChannelDetails.Id);
                if(ultraChannel is null) return;
                await ultraChannel.SendMessageAsync(message);
            }
        }

        public async Task CheckRole(ulong? roleid, DBUser dbuser, bool pro, SocketGuildUser user) {
            if(roleid is null)
                return;
            var needsRole = dbuser.EggIncAccounts.Any(y => y.SubscriptionLevel == (pro ? Ei.UserSubscriptionInfo.Types.Level.Pro : Ei.UserSubscriptionInfo.Types.Level.Standard));
            var hasRole = user.Roles.Any(x => x.Id == roleid);

            if(hasRole && !needsRole) {
                await user.RemoveRoleAsync(roleid.Value);
                _logger.LogInformation($"Removed {(pro ? "pro" : "standard")} subscription role from {user.GetCleanName()}");
            } else if(!hasRole && needsRole) {
                await user.AddRoleAsync(roleid.Value);
                _logger.LogInformation($"Added {(pro ? "pro" : "standard")} subscription role to {user.GetCleanName()}");
            }
        }
    }
}
