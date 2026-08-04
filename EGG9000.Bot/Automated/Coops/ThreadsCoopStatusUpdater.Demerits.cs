using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated.Coops {
    public partial class ThreadsCoopStatusUpdater {
        private readonly Dictionary<ulong, SocketTextChannel> _demeritChannels = [];

        public async Task HandleSleeping(UserFarmDetails user, ITextChannel coopChannel, Coop coop, ApplicationDbContext _db, Guild dbGuild) {
            if(user.Xref is null || coop.CoopEnds < DateTimeOffset.UtcNow || coop.FinishedOrFailed() || user.CoopStatus is null)
                return;

            var currentSleepStart = user.Joined ? DateTimeOffset.UtcNow.Subtract(user.OfflineTime) : coop.Created;
            var hoursSleeping = (double)user.OfflineTime.TotalMinutes / 60.0;
            var siloTimeHours = (float)(user.SiloTimeMinutes / 60.0);
            var alertTime = (30.0 - siloTimeHours) / 2 + siloTimeHours;
            var needsAlert = hoursSleeping >= alertTime;
            var timeEmpty = Math.Round(hoursSleeping - siloTimeHours, 2);

            var sleepTracking = user.Xref.SleepTracking.ToList();

            var currentSleep = sleepTracking.FirstOrDefault(x => !x.WokeUp);

            if(currentSleep == null && needsAlert) {
                currentSleep = new SleepTracking { SleepStart = currentSleepStart, LastChecked = DateTimeOffset.UtcNow, Silos = siloTimeHours, EggsShipped = user.EggsShipped, Rate = user.Rate };

                var messages = BotText.SleepingMessages;
                var random = new Random();
                var index = random.Next(messages.Count);

                if(user.DiscordUser != null) {
                    var warningText = messages[index].Replace("@name", user.DiscordUser.Mention + (timeEmpty < 0 ? $" [Empty silos in {timeEmpty} hours {coopChannel.Mention}]" : $" [Silos have been empty for {timeEmpty} hours {coopChannel.Mention}]"));
                    var dmResult = await BoolSendDm(user.DiscordUser, warningText, _db);
                    if(dmResult != DMResult.Success) {
                        var fallbackText = $"{warningText} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}";
                        _queue.EnqueueLow(() => coopChannel.SendMessageAsync(fallbackText));
                    }
                }
                sleepTracking.Add(currentSleep);
            }

            if(currentSleep != null) {
                if(currentSleepStart > currentSleep.SleepStart.AddMinutes(10)) {
                    currentSleep.WokeUp = true;
                    currentSleep.TotalHoursEmpty = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours - (currentSleep.Silos > 0 ? currentSleep.Silos : siloTimeHours);
                    currentSleep.Expected = currentSleep.EggsShipped + currentSleep.Silos * currentSleep.Rate;
                    currentSleep.Actual = user.EggsShipped;
                    user.Xref.TotalHoursSleeping = (float)(currentSleep.LastChecked - currentSleep.SleepStart).TotalHours;
                    user.Xref.HoursSleeping = 0;
                } else {
                    var demeritCheck = CoopTimingHelper.EvaluateSleepDemerit(dbGuild, hoursSleeping, timeEmpty, currentSleep.DemeritsGiven);
                    var nextDemeritAt = demeritCheck.NextDemeritAtHours;
                    var demeritChannel = await GetDemeritChannel(dbGuild);
                    var needsDemerit = demeritCheck.ShouldDemerit && demeritChannel is not null && !user.Xref.NoDemerit;
                    if(needsDemerit && user.DBUser is not null && user.DiscordUser is not null) {
                        currentSleep.DemeritsGiven++;
                        if(user.DBUser.IsFreshEgg()) {
                            var freshEggDetail = demeritCheck.OfflineBased ? $"You have been offline for {nextDemeritAt} hours." : $"Your silos have been empty for {nextDemeritAt} hours.";
                            _queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. {freshEggDetail}"));
                        } else {
                            var demerit = new Demerit {
                                When = DateTimeOffset.UtcNow,
                                AdminUserId = Guid.Empty,
                                UserId = user.DBUser.Id,
                                Id = Guid.NewGuid(),
                                Reason = demeritCheck.OfflineBased
                                    ? $"Offline for {nextDemeritAt} hours in {coop.Contract.Name}"
                                    : $"Empty silos for {nextDemeritAt} hours in {coop.Contract.Name}",
                                Details = JsonConvert.SerializeObject(new { FarmTimestemp = user.CoopStatus?.FarmInfo?.Timestamp, Silos = siloTimeHours })
                            };
                            _db.Demerit.Add(demerit);
                            await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                            var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.DBUser.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).CountAsync();
                            var demeritText = $"Demerit added to {user.DiscordUser?.Mention ?? user.DBUser.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                            if(count >= 3) {
                                demeritText = $"**{demeritText}**";
                            }
                            var coopChannelText = demeritText;
                            var demeritChannelText = $"{demeritText} {coopChannel.Mention}";
                            _queue.EnqueueLow(() => coopChannel.SendMessageAsync(coopChannelText));
                            _queue.EnqueueLow(() => demeritChannel.SendMessageAsync(demeritChannelText));
                        }
                    }
                    user.Xref.HoursSleeping = (int)Math.Floor((DateTimeOffset.UtcNow - currentSleep.SleepStart).TotalHours);
                }

                if(!currentSleep.WokeUp) {
                    currentSleep.LastChecked = DateTimeOffset.UtcNow;
                }
            }
            user.Xref.SleepTracking = sleepTracking;
        }

        public async Task HandleUnjoins(List<UserFarmDetails> usersNotJoined, List<UserWithBackup> users, Guild dbGuild, Coop coop, ApplicationDbContext _db, IThreadChannel coopChannel) {
            var demeritChannel = await GetDemeritChannel(dbGuild);
            if(demeritChannel is null) {
                return;
            }
            foreach(var userFarmDetail in usersNotJoined) {
                var user = users.FirstOrDefault(x => x.User.Id == userFarmDetail.Xref.GetID()).User;
                if(user == null || userFarmDetail.Xref.NoDemerit)
                    continue;

                var hoursToKick = CoopTimingHelper.GetHoursToKick(dbGuild, coop.Contract.cc_only);
                if(userFarmDetail.Xref.CreatedOn > DateTimeOffset.UtcNow.AddHours(-hoursToKick)) {
                    SoftRemoveFromCoop(userFarmDetail.Xref, coop);
                    await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    _queue.EnqueueLow(() => coopChannel.SendMessageAsync($"Removed {userFarmDetail.DiscordUser?.GetCleanName() ?? user.DiscordUsername} without a demerit since they were added less than {hoursToKick} hours before the co-op finished."));
                    continue;
                }

                if(user.Registered > DateTimeOffset.UtcNow.AddDays(-7)) {
                    _queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{userFarmDetail.DiscordUser?.Mention ?? user.DiscordUsername}, you failed to join this co-op. After your first week in this server you will get a demerit for failing to join an assigned co-op. Ask staff if you have any questions."));
                    continue;
                }


                await AddDemeritAndRemoveFromCoop($"Failed to join {coop.Contract.Name}", user, _db, userFarmDetail.Xref, userFarmDetail.DiscordUser, coopChannel, dbGuild, coop, true);
            }
        }

        public async Task WrongAccountWarning(UserFarmDetails user, IThreadChannel coopThread, ApplicationDbContext _db, string WrongEIID) {

            _queue.EnqueueLow(() => coopThread.SendMessageAsync($"<@{user.DBUser.DiscordId}>, it looks like you might have joined the coop with the wrong account."));
            await BoolSendDm(user.DiscordUser, $"It looks like you might have joined the coop with the wrong account in {coopThread.Mention}.", _db);

        }
        public async Task SendDMWarning(ApplicationDbContext db, SocketGuildUser discordUser, IThreadChannel coopChannel, string Message, Coop coop) {
            if(discordUser is null)
                return;

            var dmResult = await BoolSendDm(discordUser, $"{Message}: {coop.Name} for {EggIncStatics.GetEggByContract(coop.Contract, await db.GetCustomEggsAsync()).emoji} {coop.Contract.Name} - {coopChannel.Mention}", db);
            if(dmResult != DMResult.Success) {
                var fallbackMessage = $"{discordUser.Mention} {Message}: {coop.Name} for {EggIncStatics.GetEggByContract(coop.Contract, await db.GetCustomEggsAsync()).emoji} {coop.Contract.Name} - {coopChannel.Mention} {(dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)")}";
                _queue.EnqueueLow(() => coopChannel.SendMessageAsync(fallbackMessage));
            }
        }

        private void SoftRemoveFromCoop(UserCoopXref xref, Coop coop) {
            xref.Removed = true;
            xref.RemovedOn = DateTimeOffset.UtcNow;
            // Drop from the "find my coop" lookup - they no longer have this co-op to be pointed at.
            _provider.GetService<CoopAssignmentLookup>()?.Remove(xref.UserId, coop.ContractID);
        }

        public async Task AddDemeritAndRemoveFromCoop(string reason, DBUser user, ApplicationDbContext _db, UserCoopXref xref, SocketGuildUser discordUser, IThreadChannel coopChannel, Guild dbGuild, Coop coop, bool alwaysRemove) {
            var demeritChannel = await GetDemeritChannel(dbGuild);
            if(demeritChannel is null) {
                if(alwaysRemove) {
                    SoftRemoveFromCoop(xref, coop);
                }
                return;
            }
            var existingDemerit = await _db.Demerit.AnyAsync(x => x.ContractID == coop.ContractID && x.UserId == user.Id);
            if(existingDemerit || xref.JoinedCoop) {
                _queue.EnqueueLow(() => coopChannel.SendMessageAsync($"Removing {discordUser?.Mention ?? user.DiscordUsername} due to: {reason}"));
                SoftRemoveFromCoop(xref, coop);
                await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
            } else {
                SoftRemoveFromCoop(xref, coop);
                if(user.IsFreshEgg()) {
                    _queue.EnqueueLow(() => coopChannel.SendMessageAsync($"{discordUser?.Mention ?? user.DiscordUsername}: You will start receiving demerits for this 7 days after joining the server. {reason} "));
                } else {
                    var demerit = new Demerit {
                        When = DateTimeOffset.UtcNow,
                        AdminUserId = Guid.Empty,
                        UserId = user.Id,
                        Id = Guid.NewGuid(),
                        Reason = reason
                    };
                    _db.Demerit.Add(demerit);
                    await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None, logger: _logger);
                    var count = await _db.Demerit.AsQueryable().Where(x => x.UserId == user.Id && x.When > DateTimeOffset.UtcNow.AddMonths(-1)).CountAsync();
                    var demeritText = $"Demerit added to {discordUser?.Mention ?? user.DiscordUsername} for the reason: {demerit.Reason} ({count} demerits)";
                    _queue.EnqueueLow(() => coopChannel.SendMessageAsync(demeritText));
                    if(count >= 3)
                        demeritText = $"**{demeritText}**";
                    var demeritChannelText = demeritText + $" {coopChannel.Mention}";
                    _queue.EnqueueLow(() => demeritChannel.SendMessageAsync(demeritChannelText));
                }
            }

        }

        public async Task<SocketTextChannel> GetDemeritChannel(Guild dbGuild) {
            if(_demeritChannels.ContainsKey(dbGuild.Id)) return _demeritChannels[dbGuild.Id];

            var channel = await _client.GetChannelAsync(GuildChannelType.DemeritLogChannel, dbGuild);
            if(channel is not null) {
                try {
                    _demeritChannels.Add(dbGuild.Id, channel);
                } catch(ArgumentException) {

                }
                return channel;
            }

            return null;
        }
    }
}
