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

namespace EGG9000.Bot.Automated {
    public class NewContracts : _UpdaterBase<NewContracts> {
        private Words _words;
        private ContractUpdater _contractUpdater;
        public NewContracts(
            IServiceProvider provider, Words words, ContractUpdater contractUpdater
        ) : base(TimeSpan.FromMinutes(1), TimeSpan.Zero, provider) {
            Console.WriteLine("NewContracts Configured");
            _words = words;
            _contractUpdater = contractUpdater;
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            Console.WriteLine("NewContracts Run");
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var needsUpdate = false;

            var contractsResponse = await ContractsAPI.GetPeriodicalsAsync();

            if(contractsResponse == null) {
                Console.WriteLine("⚠️ERROR: Invalid Contract Response");
            } else {
                Console.WriteLine("Checking for new contracts");
                var existingContracts = await _db.Contracts.Include(x => x.GuildContracts).ToListAsync();

                var contracts = contractsResponse.Contracts.Contracts.ToList();

                CheckUpdateInterval(existingContracts);

                foreach(var contractResponse in contracts) {
                    var contract = existingContracts.FirstOrDefault(x => x.ID == contractResponse.Identifier);
                    var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

                    if(contract == null) {
                        contract = new EGG9000.Common.Database.Entities.Contract {
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
                            _response = JsonConvert.SerializeObject(contractResponse)
                        };
                        _db.Contracts.Add(contract);
                        await _db.SaveChangesAsync();

                        needsUpdate = true;
                    } else if(contract.Created < DateTime.Now.AddMonths(-3)) {
                        contract.Created = DateTime.Now;
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
                        contract._response = JsonConvert.SerializeObject(contractResponse);
                        var guildContracts = contract.GuildContracts.Where(x => x.ContractID == contract.ID);
                        _db.RemoveRange(guildContracts);
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
                    var contractCategory = await _client.GetCategoryAsync(GuildChannelType.EliteCategory, guild);
                    var contractChannel = await guild.CreateTextChannelAsync((contractResponse.MaxCoopSize > 1 ? "🐣" : "👤") + contractResponse.Identifier, x => { x.CategoryId = contractCategory.Id; x.Topic = ""; });

                    guildContract = new GuildContract {
                        ContractID = contract.ID,
                        GuildID = guild.Id,
                        Status = ContractStatus.Prefarming,
                        NumberOfCoops = 1,
                        DiscordChannelId = contractChannel.Id,
                        League = 0,
                        Created = DateTimeOffset.Now,
                        BoardingGroup = 1
                    };

                    _db.GuildContracts.Add(guildContract);
                    await _db.SaveChangesAsync();
                    _ = OrganizeAndLaunch(contract, guild, 0);
                    _ = UpdateChannel(guild, dbguild, guildContract);
                    ChangeUpdateInterval(TimeSpan.FromMinutes(5));
                } else if(guildContract.BoardingGroup < 3 && dbguild.Id == 656455567858073601) {
                    var nextLaunch = guildContract.Created.Date.AddHours(guildContract.Created.Hour + guildContract.BoardingGroup * 8);
                    Console.WriteLine(nextLaunch.ToString());
                    if(nextLaunch < DateTimeOffset.Now) {
                        guildContract.BoardingGroup++;
                        await _db.SaveChangesAsync();
                        _ = OrganizeAndLaunch(contract, guild, guildContract.BoardingGroup - 1);
                    }
                }
            }
            
        }
        
        private async Task UpdateChannel(SocketGuild guild, Guild dbguild, GuildContract targetGuildContract) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
            var backups = dbusers.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y => x.EggIncAccounts.Any(eid => eid.Id == y.EggIncId)).Select(y => new LeaderboardUser { User = x, Backup = y })).ToList();

            await _contractUpdater.UpdateContractChannel(_db, targetGuildContract, guild);
        }
        private async Task OrganizeAndLaunch(Contract contract, SocketGuild guild, int skipbg) {
            Console.WriteLine($"*!*!*!* Starting co-ops for {guild.Name} for BG{skipbg + 1} *!*!*!*");
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = await _db.DBUsers.Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
            Console.WriteLine("Getting Coops");
            var coops = await _db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == contract.ID && x.Created > DateTimeOffset.Now.AddDays(-60)).ToListAsync();
            Console.WriteLine("Sorting");
            var coopGroups = OrganizeCoops.SortUsersIntoDay1Coops(users, contract.Details, coops, skipbg);

            var dbguild = await _db.Guilds.FirstAsync(x => x.Id == guild.Id);
            foreach(var group in coopGroups.Where(x => x.bg == (skipbg + 1).ToString())) {
                Console.WriteLine($"BG {group.bg}, Grade {group.Grade}, Count {group.PotentialCoops.Count(x => x.Users.Count > 2)}");
                var coopsToCreate = group.PotentialCoops.Where(x => x.Users.Count > 2);

                await Parallel.ForEachAsync(coopsToCreate, new ParallelOptions { MaxDegreeOfParallelism = 10 }, async (coop, token) => {
                    try {
                        await CreateCoopsV2.Start(coop.Users, contract, group.Grade, guild, _words, _provider, dbguild);
                    } catch(Exception e) {
                        var frame = (new StackTrace(e, true)).GetFrame(0);
                        Console.WriteLine($"⚠️ERROR: {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()}");
                        _bugsnag.Notify(e);
                    }

                });
                await _db.SaveChangesAsync();
            }
        }

        private void CheckUpdateInterval(List<Contract> existingContracts) {
            var dayOfWeek = DateTimeOffset.Now.DayOfWeek;
            TimeSpan newUpdateInterval = TimeSpan.FromMinutes(5);
            switch(dayOfWeek) {
                case DayOfWeek.Monday:
                case DayOfWeek.Wednesday:
                case DayOfWeek.Friday:
                    var startOfQuickUpdates = DateTimeOffset.Now.Date.AddHours(10).AddMinutes(50);
                    if(DateTimeOffset.Now > startOfQuickUpdates && !existingContracts.Any(x => x.Created.Date == DateTimeOffset.Now.Date)) {
                        TimeSpan.FromSeconds(15);
                    } else {
                        TimeSpan.FromMinutes(5);
                    }
                    break;
                default:
                    newUpdateInterval = TimeSpan.FromMinutes(10);
                    break;
            }
            if(UpdateInterval != newUpdateInterval) {
                Console.WriteLine($"Setting Update Interval to {newUpdateInterval} for NewContracts");
                ChangeUpdateInterval(newUpdateInterval);
            }
        }
        
    }
}