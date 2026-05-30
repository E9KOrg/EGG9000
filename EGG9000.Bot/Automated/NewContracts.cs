using Discord.WebSocket;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.DiscordHelpersExt;
using static EGG9000.Common.Helpers.Prefarm;
using static EGG9000.Common.Services.DiscordExtensions;
using Contract = EGG9000.Common.Database.Entities.Contract;

namespace EGG9000.Bot.Automated {
    public class NewContracts(IServiceProvider provider, Words words, ContractUpdater contractUpdater, BotLogger botLogger) : _UpdaterBase<NewContracts>(TimeSpan.FromMinutes(1), TimeSpan.Zero, provider) {

        private readonly Words _words = words;
        private readonly ContractUpdater _contractUpdater = contractUpdater;
        private readonly BotLogger _botLogger = botLogger;

        public const int MIN_HOURS_TO_CREATE_COOPS = 8;

#if DEV9002 || DEBUG
        private static readonly bool _debug = true;
#else
        private static readonly bool _debug = false;
#endif

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var needsUpdate = false;

            var contractsResponse = await ContractsAPI.GetPeriodicalsAsync();

            if(contractsResponse == null) {
                _logger.LogWarning("⚠️ERROR: Invalid Contract Response");
            } else {
                var existingContracts = await _db.Contracts.Include(x => x.GuildContracts).ToListAsync(CancellationToken.None);

                var contracts = contractsResponse.Contracts.Contracts.ToList();
                var customEggs = contractsResponse.Contracts.CustomEggs?.ToList() ?? [];
                var dbCustomEggs = await _db.GetCustomEggsAsync();
                var newCustomEggs = customEggs.Where(ce => !dbCustomEggs.Any(e => e.Identifier == ce.Identifier));
                var dbNeedsUpdate = false;

                if(newCustomEggs.Any()) {
                    foreach(var newEgg in newCustomEggs) {
                        var emojiName = newEgg.GetEmojiName();
                        var existingEmotes = await _client.GetApplicationEmotesAsync();
                        var emote = existingEmotes.FirstOrDefault(e => e.Name == emojiName);
                        emote ??= await _client.CreateCustomEggEmoji(newEgg, null);

                        if(emote != null) {
                            _logger.LogInformation("New Custom Egg \"{newEgg}\" added to DB, with Emoji Name/ID: <{emoteName}:{emoteId}>", newEgg.Name, emote.Name, emote);
                            var dbEgg = new DBCustomEgg(newEgg, emote);
                            await _db.CustomEggs.AddAsync(dbEgg, CancellationToken.None);
                            dbNeedsUpdate = true;
                        }
                    }
                }

                // If any eggs had their modifiers or icons changed
                var updatedCustomEggs = customEggs.Where(ce => dbCustomEggs.Any(e => e.Identifier.Equals(ce.Identifier) && !ce.Equals(e)));
                if(updatedCustomEggs.Any()) {
                    foreach(var updatedEgg in updatedCustomEggs) {
                        var existingEgg = _db.CustomEggs.FirstOrDefault(dbe => dbe.Identifier == updatedEgg.Identifier);
                        if(existingEgg is null) continue;
                        var emote = existingEgg.GuildEmote;
                        if(existingEgg.Icon.URL != updatedEgg.Icon.Url) {
                            emote = await _client.CreateCustomEggEmoji(updatedEgg, emote);
                            if(emote != null) existingEgg.GuildEmote = emote;
                        }
                        existingEgg.Modifiers = updatedEgg.Buffs.Select(b => new DBCustomEggModifier(b)).ToList();
                        existingEgg.Icon = new(updatedEgg.Icon);
                        dbNeedsUpdate = true;
                    }
                }

                // If any eggs were previously "un-released" (didn't have a GuildContract in the db)
                var dbContractEggs = (await _db.Contracts.AsQueryable().Where(c => c.egg.ToLower() == "customegg").ToListAsync(cancellationToken))
                    .Select(x => x.Details.CustomEggId.ToLower()).Distinct();
                var newlyReleasedEggs = dbCustomEggs.Where(de => !de.Released && dbContractEggs.Contains(de.Identifier.ToLower()));
                if(newlyReleasedEggs.Any()) {
                    foreach(var releasedEgg in newlyReleasedEggs) {
                        releasedEgg.Released = true;
                    }
                    dbNeedsUpdate = true;
                }


                if(dbNeedsUpdate) {
                    await _db.SaveChangesAsyncRetry(2, logger: _logger, cancellationToken: CancellationToken.None);
                    _db._cache.InvalidateCustomEggs();
                }

                CheckUpdateInterval(existingContracts);

                foreach(var contractResponse in contracts) {
                    if(contractResponse.GradeSpecs.Any(x => x.Goals.All(y => y.TargetAmount == 0))) {
                        continue;
                    }
                    var contract = existingContracts.FirstOrDefault(x => x.ID == contractResponse.Identifier);
                    var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);


                    var json = JsonConvert.SerializeObject(contractResponse);

                    if(contract == null) {
                        // Kevin being bad causing problems - Fallback leggacy detection
                        if(!contractResponse.Leggacy) {
                            _logger.LogWarning("Contract {contractid} is not marked as leggacy, checking if it is actually new or if it's just a Kevin update without the flag", contractResponse.Identifier);
                            contractResponse.Leggacy = existingContracts.Any(c => c.ID == contractResponse.Identifier && c._response != JsonConvert.SerializeObject(contractResponse));
                        }

                        contract = new Contract {
                            ID = contractResponse.Identifier,
                            Created = DateTime.Now,
                            Description = contractResponse.Description,
                            Name = contractResponse.Name,
                            goals = JsonConvert.SerializeObject(contractResponse.Goals),
                            GoodUntil = DateTimeOffset.FromUnixTimeSeconds((long)contractResponse.ExpirationTime),
                            MaxUsers = (int)contractResponse.MaxCoopSize,
                            coop_allowed = contractResponse.CoopAllowed,
                            max_boosts = (int)contractResponse.MaxBoosts,
                            max_soul_eggs = contractResponse.MaxSoulEggs,
                            min_client_version = (int)contractResponse.MinClientVersion,
                            debug = contractResponse.Debug,
                            length_seconds = contractResponse.LengthSeconds,
                            egg = contractResponse.Egg.ToString(),
                            cc_only = contractResponse.CcOnly,
                            _response = json
                        };
                        _db.Contracts.Add(contract);
                        await _db.SaveChangesAsync(CancellationToken.None);

                        needsUpdate = true;
                        _logger.LogInformation("Contract {contractid} added", contract.ID);
                    } else if(json != contract._response || contract.Created < DateTime.Now.AddMonths(-3)) {
                        if(contract.Created < DateTime.Now.AddMonths(-3)) {
                            contract.Created = DateTimeOffset.UtcNow;
                            var guildContracts = contract.GuildContracts.Where(x => x.ContractID == contract.ID);
                            _db.RemoveRange(guildContracts);
                        }
                        _logger.LogInformation("Contract {contractid} updated", contract.ID);
                        contract._response = json;
                        contract.Description = contractResponse.Description;
                        contract.Name = contractResponse.Name;
                        contract.goals = JsonConvert.SerializeObject(contractResponse.Goals);
                        contract.GoodUntil = DateTimeOffset.FromUnixTimeSeconds((long)contractResponse.ExpirationTime);
                        contract.MaxUsers = (int)contractResponse.MaxCoopSize;
                        contract.coop_allowed = contractResponse.CoopAllowed;
                        contract.max_boosts = (int)contractResponse.MaxBoosts;
                        contract.max_soul_eggs = contractResponse.MaxSoulEggs;
                        contract.min_client_version = (int)contractResponse.MinClientVersion;
                        contract.debug = contractResponse.Debug;
                        contract.length_seconds = contractResponse.LengthSeconds;
                        contract.egg = contractResponse.Egg.ToString();
                        contract.egg_value = EggIncStatics.GetEggById(contractResponse.Egg, contract, await _db.GetCustomEggsAsync()).value;
                        contract.cc_only = contractResponse.CcOnly;
                        await _db.SaveChangesAsync(CancellationToken.None);
                        _logger.LogInformation("Contract {contractid} updated", contract.ID);
                    }

                    contract._response = JsonConvert.SerializeObject(contractResponse);
                    await _db.SaveChangesAsync(CancellationToken.None);

                    await AddContractChanelsIfNeeded(dbguilds, contract, contractResponse, _db);
                }
            }

            await _db.SaveChangesAsyncRetry(cancellationToken: CancellationToken.None);

            if(needsUpdate)
                ContractUpdater.ResetTimeStatic();
        }

        private async Task AddContractChanelsIfNeeded(List<Guild> dbguilds, Contract contract, Ei.Contract contractResponse, ApplicationDbContext _db) {
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild is null)
                    continue; var guildContract = contract.GuildContracts?.FirstOrDefault(x => x.ContractID == contract.ID && x.GuildID == guild.Id && x.League == 0);
                if(guildContract == null) {

                    var subscriptionContractCategory = await _client.GetCategoryAsync(GuildChannelType.SubscriptionContractCategory, guild);
                    var contractCategory = (contract.cc_only && subscriptionContractCategory is not null) ? subscriptionContractCategory : await _client.GetCategoryAsync(GuildChannelType.ContractCategory, guild);
                    var capturedCategoryId = contractCategory.Id;
                    var capturedChannelName = (contractResponse.MaxCoopSize > 1 ? "🐣" : "👤") + contractResponse.Identifier;
                    var contractChannel = await _queue.EnqueueLowAsync(() => guild.CreateTextChannelAsync(capturedChannelName, x => { x.CategoryId = capturedCategoryId; x.Topic = ""; }));

                    guildContract = new GuildContract {
                        ContractID = contract.ID,
                        GuildID = guild.Id,
                        Status = ContractStatus.Prefarming,
                        NumberOfCoops = 1,
                        DiscordChannelId = contractChannel.Id,
                        League = 0,
                        Created = DateTimeOffset.UtcNow,
                        BoardingGroup = 1,
                        CcOnly = contract.cc_only
                    };

                    //Ping non-ultra members who have "Ping on Ultra contract I don't have" turned on
                    //Start gathering users list
                    if(contract.cc_only) {
                        var pingableUsers = await _db.DBUsers.Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
                        pingableUsers = pingableUsers.Where(u => u.EggIncAccounts.Any(a => !a.HasActiveSubscription()
                            && a.PingForNCUltra
                            && a.Backup != null
                            && !a.Backup.Farms.Any(f => f.ContractId == contract.ID && f.Completed)
                            && !a.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.Completed)
                        )).ToList();

                        //Start forming the message
                        var validFor = DateTimeOffset.FromUnixTimeSeconds((long)contract.Details.ExpirationTime) - DateTime.Now;
                        var ultraMessageOut = $"The contract <#{contractChannel.Id}> has been released to <:ultra:1131045418319495369> Ultra Subscriber Players, and you have not completed this contract yet. The contract expires {DiscordHelpers.TimeStamper(validFor)}.";

                        foreach(var pingableUser in pingableUsers) {
                            var capturedPingUser = _client.GetUser(pingableUser.DiscordId);
                            var capturedUltraMessage = ultraMessageOut;
                            var capturedDb = _db;
                            var dmResult = await _queue.EnqueueLowAsync(() => BoolSendDm(capturedPingUser, capturedUltraMessage, capturedDb));
                            if(dmResult != DMResult.Success) {
                                _logger.LogInformation("Unable to send 'Ultra Contract Release' message to {username} {reason}.", pingableUser.DiscordUsername, dmResult == DMResult.CannotSendToUser ? "(DMs are blocked)" : "(Discord is not responding)");
                            }
                        }
                    }

                    _db.GuildContracts.Add(guildContract);
                    await _db.SaveChangesAsync();

                    await _botLogger.Log($"New Contract **{contract.Name}** detected, please wait while the bot\n1) Assigns co-ops\n2) Starts co-ops\n3) Create threads for co-ops", dbguild);

                    if(!dbguild.DisableBG && contract.ContractTime >= TimeSpan.FromHours(MIN_HOURS_TO_CREATE_COOPS)) {
                        _ = OrganizeAndLaunch(contract, guild, 0);
                    }
                    _ = UpdateChannel(guild, dbguild, guildContract);
                    ChangeUpdateInterval(TimeSpan.FromMinutes(5));
                } else if(!dbguild.DisableBG && guildContract.BoardingGroup < 4 && contract.ContractTime >= TimeSpan.FromHours(MIN_HOURS_TO_CREATE_COOPS)) {
                    var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContract.Created, "Pacific Standard Time");
                    var nextLaunch = contractDate - contractDate.TimeOfDay + TimeSpan.FromHours(9 + guildContract.BoardingGroup * 8);
                    var currentTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Pacific Standard Time");
                    if(nextLaunch < currentTime) {
                        guildContract.BoardingGroup++;
                        await _db.SaveChangesAsync();
                        if(!_debug) _ = OrganizeAndLaunch(contract, guild, guildContract.BoardingGroup - 1);
                    }
                }
            }

        }

        private async Task UpdateChannel(SocketGuild guild, Guild dbguild, GuildContract targetGuildContract) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
            var backups = dbusers.SelectMany(x => x.EggIncAccounts.Select(y => new LeaderboardUser { User = x, Backup = y.Backup })).ToList();

            await _contractUpdater.UpdateContractChannel(_db, targetGuildContract, guild, dbguild);
        }

        private async Task OrganizeAndLaunch(Contract contract, SocketGuild guild, int skipbg) {

            if(_debug) return;
            _coopsBeingCreatedService.SetCoopsAreBeingCreated(true);
            _logger.LogInformation("Starting co-ops for {guild} for BG{BG} for Contract {contract}", guild.Name, skipbg + 1, contract.Name);
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = await _db.DBUsers.Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contract.ID && x.Created > DateTimeOffset.UtcNow.AddDays(-60)).ToListAsync();
            var userCsHistoryEntries = await _db.UserCsHistoryEntries.Where(x => x.ContractIdentifier == contract.ID).ToListAsync();
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guild.Id);
            var (coopGroups, excluded) = await OrganizeCoops.SortUsersIntoDay1Coops(users, contract, coops, skipbg, userCsHistoryEntries, dbguild);

            var bgGroups = coopGroups.Where(x => x.bg == (skipbg + 1).ToString());
            int successfullyAssignedCoops = 0, failedCoops = 0;

            foreach(var group in bgGroups) {
                _logger.LogInformation("{guild} BG{bg}, Grade {grade}, Count {count} for Contract {contract}", guild.Name, group.bg, group.Grade, group.PotentialCoops.Count(x => x.Users.Count > 2), contract.Name);
                var coopsToCreate = group.PotentialCoops.Where(x => x.Users.Count > 1);

                await Parallel.ForEachAsync(coopsToCreate, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coop, token) => {
                    try {
                        await CreateCoopsV2.Start(coop.Users, contract, group.Grade, guild, _words, _provider, dbguild, (uint)skipbg + 1, contract.cc_only);
                        successfullyAssignedCoops++;
                    } catch(Exception e) {
                        failedCoops++;
                        var frame = (new StackTrace(e, true)).GetFrame(0);
                        _logger.LogError(e, "⚠️ERROR staring co-op");
                        _bugSnag.Notify(e);
                    }

                });
                await _db.SaveChangesAsync();
            }

            await _botLogger.Log($"**{contract.Name}** BG{skipbg + 1} co-ops have been assigned (coops assigned: {successfullyAssignedCoops}{(failedCoops == 0 ? "" : $", failed: {failedCoops}")}), working on starting co-ops", dbguild);
        }

        private void CheckUpdateInterval(List<Contract> existingContracts) {
            var dayOfWeek = DateTimeOffset.UtcNow.DayOfWeek;
            TimeSpan newUpdateInterval;
            switch(dayOfWeek) {
                case DayOfWeek.Monday:
                case DayOfWeek.Wednesday:
                case DayOfWeek.Friday:
                    var startOfPeriodicUpdates = DateTimeOffset.UtcNow.Date.AddHours(10).AddMinutes(40);
                    var startOfQuickUpdates = DateTimeOffset.UtcNow.Date.AddHours(10).AddMinutes(54);
                    if(DateTimeOffset.UtcNow > startOfPeriodicUpdates && !existingContracts.Any(x => x.Created.Date == DateTimeOffset.UtcNow.Date)) {
                        newUpdateInterval = TimeSpan.FromMinutes(1);
                    } else if(DateTimeOffset.UtcNow > startOfQuickUpdates && !existingContracts.Any(x => x.Created.Date == DateTimeOffset.UtcNow.Date)) {
                        newUpdateInterval = TimeSpan.FromSeconds(15);
                    } else {
                        newUpdateInterval = TimeSpan.FromMinutes(5);
                    }
                    break;
                default:
                    newUpdateInterval = TimeSpan.FromMinutes(10);
                    break;
            }
            if(UpdateInterval != newUpdateInterval) {
                _logger.LogInformation("Setting Update Interval to {newUpdateInterval} for NewContracts", newUpdateInterval);
                ChangeUpdateInterval(newUpdateInterval);
            }
        }

    }
}