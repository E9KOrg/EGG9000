using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        // Reminds co-op members who have not bought every silo their permit allows. No demerit is
        // ever issued here. The second reminder leaves a trail in the guild's SiloLog channel so
        // staff can see repeat offenders.
        private async Task ProcessSiloReminders(CoopProcessingContext ctx) {
            if(ctx.Coop.CoopEnds < DateTimeOffset.UtcNow || ctx.Coop.FinishedOrFailed()) {
                ctx.Timings.Set("3.1");
                return;
            }

            foreach(var user in ctx.CoopDetails.CoopParticipants) {
                if(user.Xref is null || !user.Xref.Joined.HasValue || !user.Joined)
                    continue;
                // FarmInfo is the live contract farm from the API, Backup is the cached snapshot
                // holding the permit level. Missing either means we cannot judge, so skip this
                // cycle without writing any state.
                if(user.CoopStatus?.FarmInfo is null || user.Backup is null || user.DiscordUser is null)
                    continue;

                var owned = (int)user.CoopStatus.FarmInfo.SilosOwned;
                var hoursSinceJoined = (DateTimeOffset.UtcNow - user.Xref.Joined.Value).TotalHours;
                var check = CoopTimingHelper.EvaluateSiloReminder(ctx.DbGuild, owned, user.Backup.PermitLevel,
                    hoursSinceJoined, user.Xref.SiloWarningFirst, user.Xref.SiloWarningSecond);

                if(check.Stage == SiloReminderStage.None)
                    continue;

                // Flags are set and saved before the send. A Discord failure afterwards costs one
                // message, where a failure before the save would re-send every cycle forever. If the
                // save itself fails, skip the sends this cycle, the flags reload false next time and
                // we try again then instead of DMing on every cycle forever.
                user.Xref.SiloWarningFirst = true;
                if(check.Stage == SiloReminderStage.Second)
                    user.Xref.SiloWarningSecond = true;
                var (saved, _) = await ctx.Db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                if(!saved)
                    continue;

                var permitName = user.Backup.PermitLevel > 0 ? "Pro Permit" : "Standard Permit";
                var message = check.Stage == SiloReminderStage.Second
                    ? $"second reminder to buy silos - you own {owned} of the {check.MaxSilos} silos your {permitName} allows, this has been logged for staff"
                    : $"reminder to buy silos - you own {owned} of the {check.MaxSilos} silos your {permitName} allows, more silos means more offline egg delivery";
                await SendDMWarning(ctx.Db, user.DiscordUser, ctx.CoopThread, message, ctx.Coop);

                if(check.Stage == SiloReminderStage.Second) {
                    // SendCustomMessage calls SendMessageAsync directly with no try/catch, unlike the
                    // DM path above it does not swallow exceptions. An archived thread or a missing
                    // permission here would otherwise throw and abort the rest of this co-op's cycle.
                    try {
                        await ChannelHelper.DetermineAndSend(_client.Gateway, ctx.DbGuild, GuildChannelType.SiloLog,
                            new() { Text = $"{user.DiscordUser.Mention} still missing silos ({owned}/{check.MaxSilos}) {check.ThresholdHours}h after joining {ctx.CoopThread.Mention}" },
                            _logger);
                    } catch(Exception e) {
                        _logger.LogWarning(e, "Failed to send silo log message for {coop} in {thread}", ctx.Coop.Name, ctx.CoopThread.Mention);
                    }
                }
            }
            ctx.Timings.Set("3.1");
        }
    }
}
