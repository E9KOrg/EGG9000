//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.Extensions.Logging;
//using EGG9000.Site.Models;
//using DiscordCoopCodes.Database;
//using Microsoft.EntityFrameworkCore;
//using DiscordCoopCodes.EggIncAPI;
//using Microsoft.AspNetCore.Identity;
//using Microsoft.AspNetCore.Authorization;
//using DiscordCoopCodes.Database.Entities;
//using Microsoft.AspNetCore.Cors;
//using System.Net.Http;
//using System.IO;
//using Google.Protobuf;
//using Discord.WebSocket;
//using Discord;
//using Newtonsoft.Json;
//using DiscordCoopCodes.Services;
//using EGG9000.Common.Database;

//namespace EGG9000.Site.Controllers {
//    public class GlobalLeaderboardController : Controller {
//        private readonly ILogger<HomeController> _logger;
//        private readonly ApplicationDbContext _db;
//        private readonly UserManager<IdentityUser> _userManager;
//        private readonly RoleManager<IdentityRole> _roleManager;
//        private readonly DiscordSocketClient _discord;
//        private readonly APILink _apiLink;

//        public GlobalLeaderboardController(
//            ILogger<HomeController> logger,
//            UserManager<IdentityUser> userManager,
//            RoleManager<IdentityRole> roleManager,
//            DiscordSocketClient discord,
//            APILink apiLink,
//            ApplicationDbContext db) {
//            _discord = discord;
//            _roleManager = roleManager;
//            _userManager = userManager;
//            _logger = logger;
//            _apiLink = apiLink;
//            _db = db;
//        }

//        //private 

//        public async Task<IActionResult> ProcessCoops() {
//            var count = 2000;

//            var times = new Dictionary<string, long>();
//            var sw = new Stopwatch();
//            sw.Restart();


//            var users = (await _db.GlobalLeaderboardUsers.AsQueryable().Select(x => x.user_id).ToListAsync()).ToHashSet();

//            Console.WriteLine($"Getting Users {sw.ElapsedMilliseconds}");
//            times.Add("Getting Users", sw.ElapsedMilliseconds);
//            sw.Restart();


//            var startUsers = users.Count;


//            var coopsToProcess = await _db.GlobalLeaderboardCoops.AsQueryable().Where(x => !x.Checked).Take(count).ToListAsync();

//            Console.WriteLine($"Getting Coops {sw.ElapsedMilliseconds}");
//            times.Add("Getting Coops", sw.ElapsedMilliseconds);
//            sw.Restart();


//            var statuses = new List<Ei.ContractCoopStatusResponseData>();
//            var tasks = coopsToProcess.Select(async (coop) => {
//                statuses.Add(await ContractsAPI.GetCoopStatus(coop.ContractID, coop.Name));
//            });

//            await Task.WhenAll(tasks);


//            Console.WriteLine($"Getting Coopstatuses {sw.ElapsedMilliseconds}");
//            times.Add("Getting Coopstates", sw.ElapsedMilliseconds);
//            sw.Restart();

//            var i = 1;
//            foreach (var coop in coopsToProcess) {
//                var status = statuses.FirstOrDefault(x => x.ContractIdentifier == coop.ContractID && x.CoopIdentifier == coop.Name);
//                if (status != null) {
//                    foreach (var participant in status.Participants) {
//                        if (!users.Contains(participant.UserId)) {
//                            var user = new GlobalLeaderboardUser {
//                                user_id = participant.UserId,
//                                NeedsUpdate = true,
//                                DegreeOfSeperation = coop.DegreeOfSeperation
//                            };
//                            users.Add(participant.UserId);
//                            _db.Add(user);
//                        }
//                    }
//                } else {
//                    coop.CheckFailed = true;
//                }
//                coop.Checked = true;
//            }


//            Console.WriteLine($"Processing Coopstatuses {sw.ElapsedMilliseconds}");
//            times.Add("Processing Coopstates", sw.ElapsedMilliseconds);
//            sw.Restart();

//            await _db.SaveChangesAsync();

//            Console.WriteLine($"Savingdb {sw.ElapsedMilliseconds}");
//            times.Add("Savingdb", sw.ElapsedMilliseconds);
//            sw.Restart();

//            var timeText = String.Join(", ", times.Select(x => $"{x.Key}: {x.Value}"));
//            timeText += $" Total: {times.Sum(x => x.Value) / 1000.0}s";
//            if (coopsToProcess.Count > 0) {
//                return Content($"<html><body>Success: Users Added {users.Count - startUsers}, Times: {timeText}<script>document.location = document.location;</script></body></html>", "text/html");
//            }
//            return Content($"Success: Needs Update Users Added {users.Count - startUsers}, Times: {timeText}");
//        }

//        public async Task<IActionResult> ProcessUsers() {
//            var times = new Dictionary<string, long>();
//            var sw = new Stopwatch();
           
//            sw.Restart();

//            var users = await _db.GlobalLeaderboardUsers.AsQueryable().Where(x => x.NeedsUpdate).Take(1000).ToListAsync();

//            Console.WriteLine($"Getting Users {sw.ElapsedMilliseconds}");
//            times.Add("Getting Users", sw.ElapsedMilliseconds);
//            sw.Restart();

//            var currentCoopsTask = _db.GlobalLeaderboardCoops.AsQueryable().Select(x => x.ContractID + x.Name).ToListAsync();

//            var backups = new List<CustomBackup>();
//            var tasks = users.Select(async (user) => {
//                var response = await _apiLink.GetBackup(user.user_id);
//                if (response != null) {
//                    backups.Add(response);
//                    //Console.WriteLine($"Backup added for: {response.Backup.UserName}");
//                } else {
//                Console.WriteLine(JsonConvert.SerializeObject(response));
//                }
//            });
//            await Task.WhenAll(tasks);

//            Console.WriteLine($"Getting backups {sw.ElapsedMilliseconds}");
//            times.Add("Getting backups", sw.ElapsedMilliseconds);
//            sw.Restart();

//            var currentCoops = (await currentCoopsTask).ToHashSet();
//            Console.WriteLine($"Getting DB Addt. {sw.ElapsedMilliseconds}");
//            times.Add("Getting DB Addt.", sw.ElapsedMilliseconds);
//            sw.Restart();


//            var addedCoops = 0;
//            var usersDone = 0;
//            var userErrors = 0;


//            foreach (var user in users) {
//                var back = backups.FirstOrDefault(x => x.EggIncId == user.user_id);

//                if (back != null && back.Game != null) {
//                    user.EggIncId = back.UserId;
//                    user.earnings_bonus = back.Game.EarningsBonus;
//                    if (double.IsInfinity(user.earnings_bonus))
//                        user.earnings_bonus = -1;
//                    user.eggs_of_prophecy = back.Game.EggsOfProphecy;
//                    user.LastUpdate = DateTimeOffset.Now;
//                    user.lifetime_cash_earned = back.Game.LifetimeCashEarned;
//                    if (double.IsInfinity(user.lifetime_cash_earned))
//                        user.lifetime_cash_earned = -1;
//                    user.soul_eggs = back.Game.SoulEggsTotal;
//                    if (double.IsInfinity(user.soul_eggs))
//                        user.soul_eggs = -1;
//                    user.user_id = back.UserId;
//                    user.user_name = back.UserName;
//                    user.NeedsUpdate = false;
//                    user.LastBackup = DateTimeOffset.FromUnixTimeSeconds((long)back.ApproxTime);



//                    var contracts = new List<Ei.LocalContract>();
//                    if(back.Contracts?.Contracts != null)
//                        contracts.AddRange(back.Contracts.Contracts);
//                    if (back.Contracts?.Archive != null)
//                        contracts.AddRange(back.Contracts.Archive);



//                    if (contracts != null) {
//                        foreach (var contract in contracts) {
//                            if (contract.CoopIdentifier?.Length > 0) {
//                                var contractCoop = contract.Contract?.Identifier + contract.CoopIdentifier;
//                                if (!currentCoops.Contains(contractCoop)) {
//                                    var coop = new GlobalLeaderboardCoop {
//                                        ContractID = contract.Contract.Identifier,
//                                        Checked = false,
//                                        Name = contract.CoopIdentifier, 
//                                        DegreeOfSeperation = user.DegreeOfSeperation + 1
//                                    };
//                                    currentCoops.Add(contractCoop);
//                                    _db.Add(coop);
//                                    addedCoops++;
//                                }
//                            }
//                        }
//                    }
//                    //Console.WriteLine($"Process Contracts {sw.ElapsedMilliseconds}");
//                    //times.Add("Process Contracts" + (userErrors + usersDone), sw.ElapsedMilliseconds);
//                    //sw.Restart();

//                    usersDone++;
//                } else {
//                    user.UpdateFailed = true;
//                    userErrors++;
//                }
//                user.LastUpdate = DateTimeOffset.Now;
//                user.NeedsUpdate = false;
//            }
//            Console.WriteLine($"Processing Users {sw.ElapsedMilliseconds}");
//            times.Add("Processing Users", sw.ElapsedMilliseconds);
//            sw.Restart();

//            await _db.SaveChangesAsync();

//            Console.WriteLine($"Updating DB {sw.ElapsedMilliseconds}");
//            times.Add("Updating DB", sw.ElapsedMilliseconds);
//            sw.Restart();


//            var count = await _db.GlobalLeaderboardUsers.AsQueryable().CountAsync(x => x.NeedsUpdate);

//            Console.WriteLine($"Get Count {sw.ElapsedMilliseconds}");
//            times.Add("Get Count", sw.ElapsedMilliseconds);
//            sw.Restart();

//            var timeText = String.Join(", ", times.Select(x => $"{x.Key}: {x.Value}"));
//            timeText += $" Total: {times.Sum(x => x.Value) / 1000.0}s";
//            if (users.Count > 0) {
//                return Content($"<html><body>Left: {count} Added Coops: {addedCoops}  UsersDone {usersDone}  UserError {userErrors} Times: {timeText}<script>document.location = document.location;</script></body></html>", "text/html");
//            }
//            //return Content($"Success: Needs Update {currentCoops.Count(x => !x.Checked)}, Users Added {users.Count - startUsers}");
//            return Content($"Left: {count} Added Coops: {addedCoops}  UsersDone {usersDone}  UserError {userErrors}  Times: {timeText}");
//        }

//        public async Task<IActionResult> Start() {
//            var users = await _db.Users.AsQueryable().ToListAsync();

//            //var addedCopps = new List<GlobalLeaderboardCoop>();
//            foreach (var user in users) {
//                if (user.Backups != null) {
//                    foreach (var back in user.Backups) {
//                        _db.Add(new GlobalLeaderboardUser {
//                            EggIncId = back.UserId,
//                            earnings_bonus = back.Game.EarningsBonus,
//                            eggs_of_prophecy = back.Game.EggsOfProphecy,
//                            LastUpdate = DateTimeOffset.Now,
//                            lifetime_cash_earned = back.Game.LifetimeCashEarned,
//                            soul_eggs = back.Game.SoulEggsTotal,
//                            user_id = back.UserId,
//                            user_name = back.UserName,
//                            NeedsUpdate = false,
//                            LastBackup = DateTimeOffset.FromUnixTimeSeconds((long)back.ApproxTime)
//                        });
//                    }
//                }
//                //var contracts = user.LastBackup?.SelectMany(x => {
//                //    var o = new List<Ei.LocalContract>();
//                //    o.AddRange(x.Contracts.Contracts);
//                //    o.AddRange(x.Contracts.Archive);
//                //    return o;
//                //});

//                //if (contracts != null) {
//                //    foreach (var contract in contracts) {
//                //        if (!addedCopps.Any(x => x.ContractID == contract.Contract.Identifier && x.Name == contract.CoopIdentifier) && contract.CoopIdentifier.Length > 0) {
//                //            addedCopps.Add(new GlobalLeaderboardCoop {
//                //                ContractID = contract.Contract.Identifier,
//                //                Checked = false,
//                //                Name = contract.CoopIdentifier
//                //            });
//                //        }
//                //    }
//                //}
//            }

//            //_db.AddRange(addedCopps);
//            await _db.SaveChangesAsync();

//            //return Content(addedCopps.Count.ToString());
//            return Content("");
//        }
//    }
//}
