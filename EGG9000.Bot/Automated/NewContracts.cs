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
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class NewContracts : _UpdaterBase {
        private readonly IConfiguration _configuration;

        public NewContracts(IConfiguration Configuration, DiscordSocketClient client,
            Bugsnag.IClient bugsnag) : base(TimeSpan.FromMinutes(1), TimeSpan.Zero, client, bugsnag) {
            Console.WriteLine("NewContracts Configured");
            _configuration = Configuration;
        }

        public override async Task Run(object state) {
            Console.WriteLine("NewContracts Run");
            var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            var needsUpdate = false;

            var contractsResponse = await ContractsAPI.GetPeriodicalsAsync();

            if(contractsResponse == null) {
                Console.WriteLine("ERROR: Invalid Contract Response");
            } else {
                Console.WriteLine("Checking for new contracts");
                var existingContracts = await _db.Contracts.Include(x => x.GuildContracts).ToListAsync();

                var contracts = contractsResponse.Contracts.Contracts.ToList();

                if(contracts.Any(x => x.StartTime > 1648789200)) {
                    var lastContract = contracts.OrderBy(x => x.StartTime).Last();
                    //var newContract = new Ei.Contract {
                    //    Egg = Egg.DarkMatter,
                    //    Identifier = "the-dark-side-2022",
                    //    Name = "The Dark Side",
                    //    Description = "With the recent confirmation of dark matter made by the Hubble telescope viewing the farthest stars, Dark Matter Eggs are needed now more than ever.",
                    //    CoopAllowed = true,
                    //    MaxCoopSize = 25,
                    //    MaxBoosts = 0,
                    //    MinutesPerToken = 30,
                    //    StartTime = lastContract.StartTime,
                    //    ExpirationTime = lastContract.ExpirationTime + 60 * 24 * 7,
                    //    LengthSeconds = lastContract.ExpirationTime + 60 * 24 * 7 - lastContract.StartTime,
                    //    MaxSoulEggs = 0,
                    //    MinClientVersion = 37,
                    //    Debug = false,
                    //    //GoalSets = new Google.Protobuf.Collections.RepeatedField<Ei.Contract.Types.GoalSet>()
                    //};

                    //var eliteGoals = new Ei.Contract.Types.GoalSet();
                    //eliteGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 25000000000000000,
                    //    RewardType = RewardType.Boost,
                    //    RewardSubType = "jimbos_orange_big",
                    //    RewardAmount = 4,
                    //    TargetSoulEggs = 100000000000
                    //});
                    //eliteGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 200000000000000000,
                    //    RewardType = RewardType.PiggyMultiplier,
                    //    RewardAmount = 2,
                    //    TargetSoulEggs = 100000000000
                    //});
                    //eliteGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 400000000000000000,
                    //    RewardType = RewardType.EggsOfProphecy,
                    //    RewardAmount = 1,
                    //    TargetSoulEggs = 100000000000
                    //});

                    //var standardGoals = new Ei.Contract.Types.GoalSet();
                    //standardGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 20000000000000,
                    //    RewardType = RewardType.Boost,
                    //    RewardSubType = "jimbos_orange_big",
                    //    RewardAmount = 2,
                    //    TargetSoulEggs = 100000000000
                    //});
                    //standardGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 150000000000000,
                    //    RewardType = RewardType.PiggyMultiplier,
                    //    RewardAmount = 2,
                    //    TargetSoulEggs = 100000000000
                    //});
                    //standardGoals.Goals.Add(new Ei.Contract.Types.Goal {
                    //    Type = GoalType.EggsLaid,
                    //    TargetAmount = 750000000000000,
                    //    RewardType = RewardType.EggsOfProphecy,
                    //    RewardAmount = 1,
                    //    TargetSoulEggs = 100000000000
                    //});
                    var smallReactors = await _db.Contracts.FirstAsync(x => x.ID == "small-reactors-2020");
                    var newContract = JsonConvert.DeserializeObject<Ei.Contract>(smallReactors._response);
                    newContract.ExpirationTime = lastContract.ExpirationTime + 60 * 24 * 7;
                    newContract.StartTime = lastContract.StartTime;
                    newContract.Identifier = "small-reactors-2022";
                    contracts.Add(newContract);
                }

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






                    foreach(var dbguild in dbguilds) {
                        var guild = _client.Guilds.First(x => x.Id == dbguild.DiscordSeverId);
                        var guildContract = contract.GuildContracts?.FirstOrDefault(x => x.ContractID == contract.ID && x.GuildID == guild.Id && x.Elite);

                        if(guildContract == null) {
                            var contractCategory = guild.GetContractsCategory(true);
                            var eliteChannel = await guild.CreateTextChannelAsync((contractResponse.MaxCoopSize > 1 ? "🐣" : "👤") + contractResponse.Identifier, x => { x.CategoryId = contractCategory.Id; x.Topic = ""; });

                            guildContract = new GuildContract {
                                ContractID = contract.ID,
                                GuildID = guild.Id,
                                Status = ContractStatus.Prefarming,
                                NumberOfCoops = 1,
                                DiscordChannelId = eliteChannel.Id,
                                Elite = true,
                                Created = DateTimeOffset.Now
                            };

                            _db.GuildContracts.Add(guildContract);
                            await _db.SaveChangesAsync();
                        }

                        var standardContractCategory = guild.GetContractsCategory(false);
                        if(standardContractCategory != null) {
                            var standardGuildContract = contract.GuildContracts?.FirstOrDefault(x => x.ContractID == contract.ID && x.GuildID == guild.Id && !x.Elite);
                            if(standardGuildContract == null) {
                                var standardChannel = await guild.CreateTextChannelAsync((contractResponse.MaxCoopSize > 1 ? "🐣" : "👤") + contractResponse.Identifier, x => { x.CategoryId = standardContractCategory.Id; x.Topic = ""; });


                                var guildContract2 = new GuildContract {
                                    ContractID = contract.ID,
                                    GuildID = guild.Id,
                                    Status = ContractStatus.Prefarming,
                                    NumberOfCoops = 1,
                                    DiscordChannelId = standardChannel.Id,
                                    Elite = false,
                                    Created = DateTimeOffset.Now
                                };

                                _db.GuildContracts.Add(guildContract2);
                                await _db.SaveChangesAsync();
                            }
                        }
                    }



                    contract._response = JsonConvert.SerializeObject(contractResponse);
                    await _db.SaveChangesAsync();
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
    }
}