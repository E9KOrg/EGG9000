using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using Humanizer;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Helpers;
using Ei;
using EGG9000.Common.Migrations;
using Polly;
using Microsoft.Data.SqlClient;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using EGG9000.Common.Contracts;
using System.Diagnostics.Contracts;
using Contract = EGG9000.Common.Database.Entities.Contract;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Automated {
    public class NewContracts : _UpdaterBase<NewContracts> {
        private Words _words;
        private ContractUpdater _contractUpdater;
        public NewContracts(
            IServiceProvider provider, Words words, ContractUpdater contractUpdater
        ) : base(TimeSpan.FromMinutes(1), TimeSpan.Zero, provider) {
            _words = words;
            _contractUpdater = contractUpdater;
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var needsUpdate = false;

            var contractsResponse = await ContractsAPI.GetPeriodicalsAsync();

            if(contractsResponse == null) {
                _logger.LogWarning("⚠️ERROR: Invalid Contract Response");
            } else {
                var existingContracts = await _db.Contracts.Include(x => x.GuildContracts).ToListAsync();

                var contracts = contractsResponse.Contracts.Contracts.ToList();

                CheckUpdateInterval(existingContracts);

                foreach(var contractResponse in contracts) {
                    if(contractResponse.GradeSpecs.Any(x => x.Goals.All(y => y.TargetAmount == 0))) {
                        continue;
                    }
                    var contract = existingContracts.FirstOrDefault(x => x.ID == contractResponse.Identifier);
                    var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

                    var json = JsonConvert.SerializeObject(contractResponse);

                    if(contract == null) {
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
                        await _db.SaveChangesAsync();

                        needsUpdate = true;
                    } else if(json != contract._response || contract.Created < DateTime.Now.AddMonths(-3)) {
                        if(contract.Created < DateTime.Now.AddMonths(-3)) {
                            contract.Created = DateTimeOffset.Now;
                            var guildContracts = contract.GuildContracts.Where(x => x.ContractID == contract.ID);
                            _db.RemoveRange(guildContracts);
                        }
                        _logger.LogInformation("Contract {0} updated", contract.ID);
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
                        contract.cc_only = contractResponse.CcOnly;
                        contract._response = json;
                        await _db.SaveChangesAsync();
                    }

                    contract._response = JsonConvert.SerializeObject(contractResponse);
                    await _db.SaveChangesAsync();

                    await AddContractChanelsIfNeeded(dbguilds, contract, contractResponse, _db);
                }
            }


            try {
                await _db.SaveChangesAsync();
            } catch(Exception) {
                await _db.SaveChangesAsync();
            }

            if(needsUpdate)
                ContractUpdater.ResetTimeStatic();
        }

        private async Task AddContractChanelsIfNeeded(List<Guild> dbguilds, Contract contract, Ei.Contract contractResponse, ApplicationDbContext _db) {
            foreach(var dbguild in dbguilds) {
                var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                var guildContract = contract.GuildContracts?.FirstOrDefault(x => x.ContractID == contract.ID && x.GuildID == guild.Id && x.League == 0);
                if(guildContract == null) {

                    var subscriptionContractCategory = await _client.GetCategoryAsync(GuildChannelType.SubscriptionContractCategory, guild);
                    var contractCategory = (contract.cc_only && subscriptionContractCategory is not null) ? subscriptionContractCategory : await _client.GetCategoryAsync(GuildChannelType.ContractCategory, guild);
                    var contractChannel = await guild.CreateTextChannelAsync((contractResponse.MaxCoopSize > 1 ? "🐣" : "👤") + contractResponse.Identifier, x => { x.CategoryId = contractCategory.Id; x.Topic = ""; });

                    guildContract = new GuildContract {
                        ContractID = contract.ID,
                        GuildID = guild.Id,
                        Status = ContractStatus.Prefarming,
                        NumberOfCoops = 1,
                        DiscordChannelId = contractChannel.Id,
                        League = 0,
                        Created = DateTimeOffset.Now,
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
                            var dmChannel = await _client.GetUser(pingableUser.DiscordId).CreateDMChannelAsync();
                            var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, ultraMessageOut);
                            var dbUser = _db.DBUsers.FirstOrDefault(u => u.DiscordId == pingableUser.DiscordId);
                            if(dbUser is not null && (retEx == null) == dbUser.DMSBlocked) {
                                dbUser.DMSBlocked = !dbUser.DMSBlocked;
                                await _db.SaveChangesAsync();
                            }
                            if(retEx != null) _logger.LogInformation("Unable to send 'Ultra Contract Release' message to {username} (DMs are blocked).", pingableUser.DiscordUsername);
                        }
                    }

                    _db.GuildContracts.Add(guildContract);
                    await _db.SaveChangesAsync();
                    if(!dbguild.DisableBG) {
                        _ = OrganizeAndLaunch(contract, guild, 0);
                    }
                    _ = UpdateChannel(guild, dbguild, guildContract);
                    ChangeUpdateInterval(TimeSpan.FromMinutes(5));
                } else if(!dbguild.DisableBG && guildContract.BoardingGroup < 4) {
                    var contractDate = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(guildContract.Created, "Pacific Standard Time");
                    var nextLaunch = (contractDate - contractDate.TimeOfDay) + TimeSpan.FromHours(9 + guildContract.BoardingGroup * 8);
                    var currentTime = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.Now, "Pacific Standard Time");
                    if(nextLaunch < currentTime) {
                        guildContract.BoardingGroup++;
                        await _db.SaveChangesAsync();
#if !DEV9002
                        _ = OrganizeAndLaunch(contract, guild, guildContract.BoardingGroup - 1);
#endif
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
#if DEV9002
            return;
#endif
            _logger.LogInformation("Starting co-ops for {guild} for BG{BG} for Contract {contract}", guild.Name, skipbg + 1, contract.Name);
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = await _db.DBUsers.Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contract.ID && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
            var userCsHistoryEntries = await _db.UserCsHistoryEntries.Where(x => x.ContractIdentifier == contract.ID).ToListAsync();
            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guild.Id);
            var sortedGroupd = await OrganizeCoops.SortUsersIntoDay1Coops(users, contract, coops, skipbg, userCsHistoryEntries, dbguild);

            foreach(var group in sortedGroupd.coopGroups.Where(x => x.bg == (skipbg + 1).ToString())) {
                _logger.LogInformation("{guild} BG{bg}, Grade {grade}, Count {count} for Contract {contract}", guild.Name, group.bg, group.Grade, group.PotentialCoops.Count(x => x.Users.Count > 2), contract.Name);
                var coopsToCreate = group.PotentialCoops.Where(x => x.Users.Count > 1);

                await Parallel.ForEachAsync(coopsToCreate, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coop, token) => {
                    try {
                        await CreateCoopsV2.Start(coop.Users, contract, group.Grade, guild, _words, _provider, dbguild, (uint)skipbg + 1, contract.cc_only);
                    } catch(Exception e) {
                        var frame = (new StackTrace(e, true)).GetFrame(0);
                        _logger.LogError(e, "⚠️ERROR staring co-op");
                        _bugsnag.Notify(e);
                    }

                });
                await _db.SaveChangesAsync();
            }
        }

        private void CheckUpdateInterval(List<Contract> existingContracts) {
            var dayOfWeek = DateTimeOffset.Now.DayOfWeek;
            TimeSpan newUpdateInterval;
            switch(dayOfWeek) {
                case DayOfWeek.Monday:
                case DayOfWeek.Wednesday:
                case DayOfWeek.Friday:
                    var startOfPeriodicUpdates = DateTimeOffset.Now.Date.AddHours(10).AddMinutes(40);
                    var startOfQuickUpdates = DateTimeOffset.Now.Date.AddHours(10).AddMinutes(54);
                    if(DateTimeOffset.Now > startOfPeriodicUpdates && !existingContracts.Any(x => x.Created.Date == DateTimeOffset.Now.Date)) {
                        newUpdateInterval = TimeSpan.FromMinutes(1);
                    } else if(DateTimeOffset.Now > startOfQuickUpdates && !existingContracts.Any(x => x.Created.Date == DateTimeOffset.Now.Date)) {
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