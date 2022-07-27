using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Humanizer;

using Microsoft.EntityFrameworkCore;

using Newtonsoft.Json;

using Nito.AsyncEx;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Commands {
    public static class ContractCommandsSlash {
        [SlashCommand(Description = "Makes a co-op public", AdminOnly = true)]
        public static async Task MakePublic(FauxCommand command, ApplicationDbContext db) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = 30,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = true,
                RequestingUserId = ContractsAPI.UserId
            }, ContractsAPI.UserId);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now public.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be public.");
                //await command.RespondAsync($"ERROR: {response.Message}");
            }
        }

        [SlashCommand(Description = "Makes this co-op private", AdminOnly = true)]
        public static async Task MakePrivate(FauxCommand command, ApplicationDbContext db) {
            var name = new Regex(@"\w+").Match(command.Channel.Name.ToLower()).Value;
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = 30,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = true,
                RequestingUserId = ContractsAPI.UserId
            }, ContractsAPI.UserId);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now private.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be private.");
            }
        }

        [SlashCommand(Description = "Adds prefarmers from selected contract to this channel", AdminOnly = true)]
        public static async Task AddPrefarmers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }
            await command.RespondAsync($"Please wait...adding prefarmers");

            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

            var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

            foreach(var user in coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants)) {
                await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
            }

            foreach(var user in coopsBreakdown.ExpiredFarms) {
                await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
            }

            await command.Channel.SendMessageAsync($"{coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Count()} prefarmers added");
        }


        [SlashCommand(Description = "Adds an outside co-op so you can track it's progress", AdminOnly = true, AllowFarmHand = true)]
        public static async Task AddCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }

            var status = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coopname.ToLower());
            if(status != null && status.Success) {

                var coop = new Coop {
                    ContractID = guildContract.ContractID,
                    Created = DateTimeOffset.Now,
                    GuildId = guildContract.GuildID,
                    Name = coopname,
                    MaxUsers = guildContract.Contract.MaxUsers,
                    Status = CoopStatusEnum.WaitingOnAssigned,
                    League = (UInt32)(guildContract.Elite ? 0 : 1),
                    CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
                };
                db.Coops.Add(coop);
                await db.SaveChangesAsync();
                await command.RespondAsync($"Co-op Added: {coopname} for {((SocketTextChannel)contractchannel).Mention}");
                return;
            } else {
                await command.RespondAsync($"ERROR: Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention}).");
                return;
            }
        }



        [SlashCommand(Description = "Fix a users reference in a co-op when they are showing as an alien", AdminOnly = true)]
        public static async Task FixReference(FauxCommand command, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ApplicationDbContext db, [SlashParam] SocketGuildUser targetuser, [SlashParam(Description = "Egg Inc Name, will match partial name")] string eggincname) {
            //var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            var xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id && !x.JoinedCoop);
            if(xref == null) {
                xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id);
            }
            if(xref == null) {
                await command.RespondAsync($"ERROR: Bot error - Unable to find user assignment to co-op");
                return;
            }


            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(eggincname.ToLower()));
            if(t == null) {
                await command.RespondAsync($"ERROR: Bot error - Unable to find user in co-op. You can use a partial in-game name.");
                return;
            }

            //var newxref = new UserCoopXref {
            //    AddedToChannel = true,
            //    CoopId = xref.CoopId,
            //    CreatedOn = xref.CreatedOn,
            //    JoinedCoop = false,
            //    Starter = false,
            //    UserId = xref.GetID(),
            //    WaitingOnStarter = false,
            //    EggIncId = t.GetID(),
            //    RefEggIncId = xref.EggIncId,
            //    WasAssigned = true
            //};

            //db.Remove(xref);
            //db.Add(newxref);

            xref.FixedUserName = t.UserName;
            await db.SaveChangesAsync();

            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
            var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
            await coopStatusUpdater.SendUpdate(targetCoop.Id, guild, users, dbguild, default, db);

            await command.RespondAsync($"Fixed {targetuser.Mention} reference.");
        }

        [SlashCommand(Description = "Move a user to a co-op. Tag channel or type co-op name", AdminOnly = true)]
        public static async Task MoveToCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketGuildUser user, [SlashParam(Required = false)] SocketChannel coop, [SlashParam(Required = false)] string coopname, [SlashParam(Required = false)] int accountnumber) {
            Coop targetCoop;
            if(coop == null) {
                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.Name.ToLower() == coopname.ToLower());
                if(targetCoop == null) {
                    await command.RespondAsync($"ERROR: Unable to find co-op name {coopname}");
                    return;
                }
                var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;
                coop = _client.Guilds.First(x => x.Id == guildId).GetChannel(targetCoop.DiscordChannelId);
            } else {
                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.DiscordChannelId == coop.Id);
            }
            //var contract = await db.GuildContracts.Include(x => x.Contract).ThenInclude(x => x.Coops).FirstAsync(x => x.ContractID == targetCoop.ContractID);

            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == user.Id);



            var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == user.Id).ToListAsync();
            if(xrefs.Count == dbuser.EggIncIds.Count && xrefs.First().JoinedCoop) {
                await command.RespondAsync($"ERROR: {user.Mention} has already joined {xrefs.First().Coop.Name}");
                return;
            }

            Guid dbuserid;
            string EggIncId;
            var eggIncName = "";
            if(xrefs.Count == 0 || dbuser.EggIncIds.Count > 1) {
                dbuserid = dbuser.Id;
                if(dbuser.EggIncIds.Count > 1) {
                    var contract = await db.Contracts.AsQueryable().FirstAsync(x => x.ID == targetCoop.ContractID);
                    var prefarms = dbuser.Backups.Select(b => Prefarm.BackupToPreFarm(new LeaderboardUser { Backup = b, User = dbuser }, contract)).Where(x => x.Elite ? targetCoop.League == 0 : targetCoop.League == 1).ToList();

                    prefarms = prefarms.Where(x => !xrefs.Any(y => y.EggIncId == x.EggIncId)).ToList();
                    if(prefarms.Count == 1) {
                        EggIncId = prefarms.First().EggIncId;
                        eggIncName = $" ({prefarms.First().Backup.UserName})";
                    } else if(accountnumber <= 1) {
                        var count = 1;
                        await command.RespondAsync($"User has multiple accounts, please specifiy which account \n{String.Join("\n", prefarms.Select(x => $"{count++} {x.Backup.UserName} Projected: {x.Projected.ToEggString()}"))}");
                        return;
                    } else {
                        EggIncId = prefarms[accountnumber - 1].EggIncId;
                        eggIncName = $" ({prefarms[accountnumber - 1].Backup.UserName})";
                    }
                    xrefs.Clear();
                } else {
                    EggIncId = dbuser.EggIncIds.First().Id;
                }
            } else {
                dbuserid = xrefs.First().GetID();
                EggIncId = xrefs.First().EggIncId;
            }

            var newxref = await CreateCoops.MoveUser(targetCoop, dbuserid, EggIncId, eggIncName, user, dbuser, (SocketTextChannel)coop, (SocketTextChannel)command.Channel);

            if(newxref == null) {
                await command.RespondAsync($"ERROR: Unable to add permission for {user.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }

            db.RemoveRange(xrefs);
            db.Add(newxref);

            await command.RespondAsync($"Moved {user.Mention}{eggIncName} to {((ITextChannel)coop).Mention}");
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Remove user from co-op (only works if the bot doesn't see them as joined", AdminOnly = true)]
        public static async Task RemoveFromCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketUser user) {
            UserCoopXref xref;
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync($"ERROR: Please use in a co-op channel");
                return;
            }

            xref = await db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

            if(xref == null) {
                await command.RespondAsync($"ERROR: Unabled to find user in co-op");
                return;
            }

            db.Remove(xref);
            await db.SaveChangesAsync();

            await command.RespondAsync($"Removed {user?.Mention ?? xref.User.DiscordUsername} from co-op");

        }

        [SlashCommand(Description = "Delete a contract channel (Please use this instead of deleting the channel in discord)", AdminOnly = true)]
        public static async Task DeleteContract(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract, use only in contract channels.");
                return;
            }
            guildContract.DeletedChannel = true;
            await db.SaveChangesAsync();
            var channel = (SocketTextChannel)command.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand(Description = "Makes it so the bot won't notify you for contracts that don't have an Egg of Prophecy (PE)")]
        public static async Task SkipNoEggOfProphecy(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync($"ERROR: Unable to find user");
                return;
            }

            dbUser.SkipNoPE = true;
            await db.SaveChangesAsync();
            await command.RespondAsync($"You are set to skip contracts without <:Egg_of_Prophecy_PE:669981330477547580>, what this means is you won't get a demerit for not participating in this contract. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server.", ephemeral: true);
        }

        [SlashCommand(Description = "Bot will notify you of contracts even without an Egg of Prophecy (PE)")]
        public static async Task UnSkipNoPeEggOfProphecy(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync($"ERROR: Unable to find user");
                return;
            }

            dbUser.SkipNoPE = false;
            await db.SaveChangesAsync();
            await command.RespondAsync($"You are NO longer set to skip contracts without an <:Egg_of_Prophecy_PE:669981330477547580>.", ephemeral: true);
        }

        [SlashCommand(Description = "Stop the bot from pinging you for the tagged contract.")]
        public static async Task Skip(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }

            var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");
            skipList.Add(command.User.Id);

            guildContract.Skip = JsonConvert.SerializeObject(skipList);
            await db.SaveChangesAsync();
            await command.RespondAsync($"{command.User.Mention} is set to skip {((SocketTextChannel)contractchannel).Mention}. **If you have already started the contract and don't exit it, you will still get a demerit**. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server. Did you know there is a new command !skipNoPe that will allow you to automatically skip all contracts without an <:Egg_of_Prophecy_PE:669981330477547580>?", ephemeral: true);
        }


        private static async Task _start(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, int percent, APILink _apiLink, Words _words) {
            var coopCount = 0;

            await command.RespondAsync($"Working...");
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                return;
            }

            var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

            foreach(var coopDetail in coopsBreakdown.PotentialCoops) {
                var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
                var cooppercent = (decimal)(coopDetail.CoopParticipants.Sum(x => x.Projected) / targetAmount) * 100m;
                if(cooppercent < percent) {
                    continue;
                }

                var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);
                var coop = await CreateCoops.Start(coopDetail.CoopParticipants, guildContract, guild, _words, db);

                coopCount++;
            }

            if(percent == 0 || coopCount == coopsBreakdown.PotentialCoops.Count) {
                guildContract.Status = ContractStatus.Creating;
                //await ((SocketGuildChannel)command.Channel).ModifyAsync(x => x.Name = "📥" + guildContract.ContractID);
            } else {
                //await ((SocketGuildChannel)command.Channel).ModifyAsync(x => x.Name = "🐣📥" + guildContract.ContractID);
            }


            guildContract.NumberOfCoops = Math.Max(0, guildContract.NumberOfCoops - coopCount);
            await db.SaveChangesAsync();
            await command.Channel.SendMessageAsync($"Started {coopCount} co-ops");
        }

        [SlashCommand(Description = "Start a user's co-op", AdminOnly = true)]
        public static async Task StartUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] SocketGuildUser user, [SlashParam(Description = "Fill the co-op with other users")] bool fillcoop) {
            await _StartUser(command, db, _client, _apiLink, _words, fillcoop, user);
        }


        [SlashCommand(Description = "Start co-ops with users above a certain percent and backfill with low EB users", AdminOnly = true)]
        public static async Task StartFill(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam(Description = "Percent at which a user will be started")] int percenttostart) {
            if(percenttostart < 120) {
                await command.RespondAsync($"Minumum percent for /startfill is 120%");
                return;
            }
            await _StartUser(command, db, _client, _apiLink, _words, true, percent: percenttostart);
        }

        [SlashCommand(Description = "Start fire co-op and backfill with low EB users", AdminOnly = true)]
        public static async Task StartFire(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
            await _StartUser(command, db, _client, _apiLink, _words, true, startFire: true);
        }




        public static async Task _StartUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, bool fill, SocketGuildUser user = null, int? percent = null, bool startFire = false) {
            var coopCount = 0;

            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                return;
            }

            await command.RespondAsync($"Working...");


            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);




            var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

            var prefarms = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Where(x => x.ProjectedPercent < 10).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;


            if(user == null) {
                List<CoopDetails> coopsToStart;
                if(startFire) {
                    coopsToStart = coopsBreakdown.PotentialCoops.Where(x => x.IsFire || x.IsDoubleFire).ToList();
                } else {
                    coopsToStart = coopsBreakdown.PotentialCoops.Where(x => x.PercentProjected >= percent).ToList();
                }
                foreach(var coop in coopsToStart) {
                    var carry = coop.CoopParticipants.OrderByDescending(x => x.Projected).First();
                    var carryWithAlts = coop.CoopParticipants.Where(x => x.DiscordUser.Id == carry.DiscordUser.Id).ToList();
                    var participants = await _startUserCreateCoop(carryWithAlts, guildContract, prefarms, db, guild, _words, true);
                    var numberRemoved = prefarms.RemoveAll(x => participants.Any(y => y == x));
                    coopCount++;
                }
                //} else if(user == null) {
                //    var usersAbovePercent = prefarms.Where(p => (p.Projected / targetAmount) * 100 >= percent).ToList();
                //    usersAbovePercent.ForEach(x => prefarms.Remove(x));
                //    prefarms = prefarms.Where(p => (p.Projected / targetAmount) * 100 < 5).ToList();
                //    foreach(var userAbovePercent in usersAbovePercent) {
                //        var participants = await _startUserCreateCoop(userAbovePercent, guildContract, prefarms, db, guild, _words);
                //        participants.ForEach(x => prefarms.Remove(x));
                //        coopCount++;
                //    }
            } else {
                var prefarm = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants.Where(y => y.DiscordUser.Id == user.Id)).First();
                prefarms.Remove(prefarm);
                _ = await _startUserCreateCoop(new List<UserFarmDetails> { prefarm }, guildContract, prefarms, db, guild, _words, fill);
                coopCount++;
            }

            guildContract.NumberOfCoops = Math.Max(1, guildContract.NumberOfCoops - coopCount);
            await db.SaveChangesAsync();

            try {
                await command.Channel.SendMessageAsync($"Finished Starting {coopCount} coop{(coopCount > 1 ? "s" : "")}");
            } catch(Exception) {
                //Possible message was deleted in the meantime
            }
        }

        private static async Task<List<UserFarmDetails>> _startUserCreateCoop(List<UserFarmDetails> existingUsers, GuildContract guildContract, List<UserFarmDetails> otherUsers, ApplicationDbContext db, SocketGuild guild, Words _words, bool fill = true) {
            var participants = new List<UserFarmDetails>();
            //if(guildContract.Contract.MaxUsers > 4) {
            //    participants.AddRange(prefarms.Where(x => x.DiscordId == user.DiscordId));
            //}
            participants.AddRange(existingUsers);
            if(fill) {
                participants.AddRange(otherUsers.TakeLast(guildContract.Contract.MaxUsers - participants.Count));
            }

            var coop = await CreateCoops.Start(participants, guildContract, guild, _words, db);
            return participants;
        }


        [SlashCommand(Description = "Start an empty co-op", AdminOnly = true)]
        public static async Task StartEmpty(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"Unable to find contract, is this run in a contract channel?");
                return;
            }
            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

            var coop = await CreateCoops.Start(new List<UserFarmDetails>(), guildContract, guild, _words, db);
            await db.SaveChangesAsync();
            await command.RespondAsync($"Empty co-op created");
            return;
        }

        [SlashCommand(Description = "Start all co-ops as-is that are above a certain percent", AdminOnly = true)]
        public static async Task StartPercent(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] int percent) {
            await _start(command, db, _client, percent, _apiLink, _words);
        }

        [SlashCommand(Description = "Start all co-ops", AdminOnly = true)]
        public static async Task StartAll(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
            await _start(command, db, _client, 0, _apiLink, _words);
        }


        //public static async Task SetNumber(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //        return;

        //    }

        //    if(args.Length == 0) {
        //        await command.RespondAsync($"ERROR: Please include the number of coops you would like the bot to create");
        //        return;
        //    }

        //    var number = Int32.Parse(args[0]);

        //    guildContract.NumberOfCoops = number;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"# of coops set to {number}");
        //}

        [SlashCommand(Description = "Set the number of potential co-ops", AdminOnly = true)]
        public static async Task SetNumber(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(Description = "Number of potental co-ops (excludes ones already created)")] int numberofcoops) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                return;

            }


            guildContract.NumberOfCoops = numberofcoops;
            await db.SaveChangesAsync();
            await command.RespondAsync($"# of coops set to {numberofcoops}");
            //ResetUpdateTimer();
        }

        //public static async Task Update(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    await command.RespondAsync($"Updating...");
        //    ContractUpdater.ResetTimeStatic();
        //}

        private static readonly AsyncLock joinLock = new AsyncLock();


        [SlashCommand(Description = "Join a Co-op", ParentCommand = "a", AdminOnly = true, AllowFarmHand = true)]
        public static async Task Join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam] SocketGuildUser targetUser) {
            await _join(command, db, _apiLink, _client, targetUser);
        }
        [SlashCommand(Description = "Join a Co-op (Only usable in a #spots thread)")]
        public static async Task Join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client) {
            await _join(command, db, _apiLink, _client, command.User);
        }
        public static async Task _join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, IUser targetUser) {

            await command.RespondAsync("Please wait finding a co-op...");
            using(await joinLock.LockAsync()) {
                var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == targetUser.Id);
                SocketThreadChannel thread;
                try {
                    thread = (SocketThreadChannel)command.Channel;
                } catch(Exception ex) when(ex is AggregateException || ex is InvalidCastException) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = "ERROR: Unable to find contract details, this command only works in a contract spots thread.");
                    return;
                }
                var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
                if(guildContract == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
                    return;
                }
                var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
                var rawusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildContract.GuildID).Select(x => new {
                    x.DiscordId,
                    x.DiscordUsername,
                    x.GuildId,
                    x.Id,
                    x._CustomBackups,
                    x._eggIncIds,
                    x.TempDisabled
                }).ToListAsync();
                var dbusers = rawusers.Select(x => new DBUser { TempDisabled = x.TempDisabled, DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds });
                var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                    User = y,
                    Backup = x
                })).ToList();
                var prefarmers = GetPrefarmers(backups, guildContract);

                var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
                //var alienBackups = await GetBackupsForAliens(coops, prefarmers, guildContract.Contract, _apiLink);
                var alienBackups = new List<UserPreFarm>();
                var currentCoops = coops.Select(coop => {
                    var prefarms = GetPrefarmsForCoop(coop, prefarmers, alienBackups, guildContract.Contract);
                    return new {
                        Coop = coop,
                        Prefarms = prefarms,
                        TimeRemaining = GetTimeRemainingValue(targetAmount, prefarms.Sum(x => x.Rate), prefarms.Sum(x => x.EggsPaidFor + x.Rate * x.TimeSinceUpdate.TotalSeconds)),
                        HasSpots = guildContract.Contract.Details.MaxCoopSize > prefarms.Count
                    };
                }).Where(x => !x.Coop.Finished && x.HasSpots).OrderByDescending(x => x.HasSpots).ThenBy(x => x.TimeRemaining).ToList();

                if(currentCoops.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find open co-op, all spots may have been filled.");
                    return;
                }


                Coop targetCoop = null;
                SocketTextChannel targetChannel = null;

                foreach(var coop in currentCoops) {
                    var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
                    if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize) {
                        targetChannel = (SocketTextChannel)_client.GetChannel(coop.Coop.DiscordChannelId);
                        if(targetChannel != null) {
                            targetCoop = coop.Coop;
                            break;
                        }
                    }

                }
                if(targetCoop == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find open co-op, all spots may have been filled.");
                    return;

                }

                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == dbuser.DiscordId && x.CreatedOn > DateTimeOffset.Now.AddMonths(-2)).ToListAsync();
                if(xrefs.Count == dbuser.EggIncIds.Count) {
                    if(xrefs.Count == 1 && (xrefs.First().Coop.DeletedChannel || xrefs.First().Coop.FinishedOrFailed)) {
                        db.UserCoopXrefs.Remove(xrefs.First());
                        await db.SaveChangesAsync();
                        xrefs = new List<UserCoopXref>();
                    } else {
                        var coopLinks = string.Join(", ", xrefs.Select(x => $"<https://discord.com/channels/{x.Coop.OverflowGuildId}/{x.Coop.DiscordChannelId}>"));
                        await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: <@{dbuser.DiscordId}> has already been assigned {(xrefs.Count > 1 ? "the co-ops" : "a co-op")}. You can get to the co-op by clicking the following link {coopLinks}, if this co-op has already finished contact staff and we can get it fixed.");
                        return;
                    }
                }

                string EggIncId;
                var eggIncName = "";
                if(xrefs.Count == 0 || dbuser.EggIncIds.Count > 1) {
                    if(dbuser.EggIncIds.Count > 1) {
                        var contract = await db.Contracts.AsQueryable().FirstAsync(x => x.ID == targetCoop.ContractID);
                        var prefarms = prefarmers.Where(x => x.Elite == guildContract.Elite && x.DatabaseId == dbuser.Id && !xrefs.Any(y => y.EggIncId == x.EggIncId)).ToList();
                        if(prefarms.Count == 0) {
                            await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Looks like all prefarms for <@{dbuser.DiscordId}> have been assigned.");
                            return;
                        }
                        EggIncId = prefarms.First().EggIncId;
                        eggIncName = $" ({prefarms.First().Backup.UserName})";
                    } else {
                        EggIncId = dbuser.EggIncIds.First().Id;
                    }
                } else {
                    EggIncId = xrefs.First().EggIncId;
                }

                var newxref = await CreateCoops.MoveUser(targetCoop, dbuser.Id, EggIncId, eggIncName, targetUser, dbuser, (SocketTextChannel)targetChannel, (SocketTextChannel)command.Channel);

                if(newxref == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"{command.User.Mention} looks like you are not in the overflow servers. **Make sure and join the overflow servers in <#775558629671698442> to see your co-op, it's in {targetChannel.Guild.Name}**. Once you've joined the overflows use this link to get to your co-op 👉 https://discord.com/channels/{targetCoop.OverflowGuildId}/{targetCoop.DiscordChannelId}");
                    return;
                }

                db.RemoveRange(xrefs);
                db.Add(newxref);

                await db.SaveChangesAsync();
                await command.ModifyOriginalResponseAsync(x => x.Content = $"Moved you to a co-op, link to the co-op 👉 https://discord.com/channels/{targetCoop.OverflowGuildId}/{targetCoop.DiscordChannelId}");
            }
        }

        [SlashCommand(Description = "Move prefarmers to co-ops ending >24h", AdminOnly = true)]
        public static async Task MovePrefarmers(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam(Required = false)] int overrideperecent = 5) {
            if(overrideperecent == 0)
                overrideperecent = 5;
            await command.RespondAsync("Please wait moving prefarmers...");
            using(await joinLock.LockAsync()) {

                //var discordUser = command.User;
                //var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == discordUser.Id);
                var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
                if(guildContract == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                    return;
                }
                var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
                var rawusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildContract.GuildID).Select(x => new {
                    x.DiscordId,
                    x.DiscordUsername,
                    x.GuildId,
                    x.Id,
                    x._CustomBackups,
                    x._eggIncIds,
                    x.TempDisabled
                }).ToListAsync();
                var dbusers = rawusers.Select(x => new DBUser { TempDisabled = x.TempDisabled, DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds });
                var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new UserWithBackup {
                    User = y,
                    Backup = x
                })).ToList();


                var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
                var coopsBreakdown = Prefarm.GetBreakdown(coops, backups, guildContract, _client);

                var currentCoops = coopsBreakdown.ExistingCoops.Where(x => x.TimeRemaining > TimeSpan.FromHours(24) && !x.Coop.FinishedOrFailed && x.HasSpots).OrderBy(x => x.TimeRemaining).ToList();


                if(currentCoops.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find open co-op, all spots may have been filled.");
                    return;
                }

                var allPrefarms = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).OrderBy(x => x.Backup.EarningsBonus).ToList();
                var discordIds = allPrefarms.Select(p => p.DiscordUser.Id);

                var coopsAbove100Percent = currentCoops.Where(x => x.PercentProjected > 100);


                var prefarmsAbovePercent = allPrefarms.Where(p => p.ProjectedPercent >= overrideperecent);

                if(prefarmsAbovePercent.Any()) {
                    await DiscordMessageSplitter.SendMessageSplitAsync(
                        command.Channel,
                        $"The following users are above {overrideperecent}% and won't be moved: \n{string.Join("\n", prefarmsAbovePercent.OrderBy(x => x.Projected).Select(p => $"{p.Name} {Math.Round(p.ProjectedPercent)}%"))}",
                        "\n"
                        );
                }

                allPrefarms = allPrefarms.Where(p => p.ProjectedPercent < overrideperecent).ToList();

                foreach(var user in allPrefarms) {
                    var dbuser = dbusers.First(x => x.DiscordId == user.DiscordUser.Id);
                    var discordUser = _client.GetUser(user.DiscordUser.Id);
                    var added = false;
                    foreach(var coop in currentCoops.Where(x => x.HasSpots)) {
                        var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
                        if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize) {
                            var targetCoop = coop.Coop;
                            var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == dbuser.DiscordId).ToListAsync();
                            if(xrefs.Count() == dbuser.EggIncIds.Count && xrefs.First().JoinedCoop) {
                                await command.Channel.SendMessageAsync($"ERROR: Unable to add {user.Name}, they are already assigned a co-op");
                                break;
                            }

                            var eggIncName = "";
                            if(dbuser.EggIncIds.Count > 1) {
                                eggIncName = $" ({user.Backup.UserName})";
                            }

                            var channel = (SocketTextChannel)_client.GetChannel(targetCoop.DiscordChannelId);
                            var newxref = await CreateCoops.MoveUser(targetCoop, dbuser.Id, user.EggIncId, eggIncName, discordUser, dbuser, (SocketTextChannel)channel, (SocketTextChannel)command.Channel);

                            if(newxref == null) {
                                await command.Channel.SendMessageAsync($"ERROR: Unable to add permission for {discordUser.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                                continue;
                            }


                            db.RemoveRange(xrefs);
                            db.Add(newxref);

                            await db.SaveChangesAsync();
                            await command.Channel.SendMessageAsync($"Moved {user.Name} to a co-op");
                            added = true;
                            coop.CoopParticipants.Add(user);
                            break;
                        }

                    }
                    if(!added) {
                        await command.Channel.SendMessageAsync($"ERROR: Unable to find open co-op, all spots may have been filled.");
                        break;

                    }

                }
                await command.DeleteResponseFix();
            }
        }


        [SlashCommand(Description = "Ping people to add them to a spots thread", AdminOnly = true)]
        public static async Task SpotPings(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam(Required = false)] int overridepercent = 5) {
            if(overridepercent == 0)
                overridepercent = 5;
            if(command.Channel is not SocketThreadChannel) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
                return;
            }
            var thread = (SocketThreadChannel)command.Channel;
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
                return;
            }
            await command.RespondAsync("Pinging users and removing existing pings that aren't needed", ephemeral: true);
            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;

            var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

            var threadMessages = await command.Channel.GetMessagesAsync(limit: 500).FlattenAsync();



            var usersWithFarm = new List<UserFarmDetails>();
            usersWithFarm.AddRange(coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants));
            usersWithFarm.AddRange(coopsBreakdown.ExpiredFarms);

            var oldPings = threadMessages.Where(m => m.MentionedUserIds.Count == 1 && usersWithFarm.Any(u => u.DiscordUser.Id == m.MentionedUserIds.First()) && m.CreatedAt.AddHours(12) < DateTimeOffset.Now);



            var usersToPing = usersWithFarm.Where(u => u.ProjectedPercent < overridepercent && !threadMessages.Any(m => m.MentionedUserIds.Any(mu => mu == u.DiscordUser.Id))).ToList();
            var pingsToRemove = threadMessages.Where(m =>
             (m.MentionedUserIds.Count == 1 && !usersWithFarm.Any(u => u.DiscordUser.Id == m.MentionedUserIds.First()))
            //|| (!usersWithFarm.Any(u => m.Author.Id == u.DiscordId) && !m.Author.IsBot && !m.Author.rol)

            ).ToList();


            var joinCommands = threadMessages.Where(m => (m.Interaction != null && m.Interaction.Type == InteractionType.ApplicationCommand) || m.Content == "/join");
            var usersAbovePercent = usersWithFarm.Where(u => u.ProjectedPercent >= overridepercent).ToList().OrderBy(x => x.Projected);

            foreach(var user in usersToPing) {
                await command.Channel.SendMessageAsync($"<@{user.DiscordUser.Id}>");
            }

            var messagesToDelete = new List<IMessage>();

            foreach(var ping in pingsToRemove) {
                messagesToDelete.Add(ping);
            }

            foreach(var oldPing in oldPings) {
                var user = usersWithFarm.First(x => x.DiscordUser.Id == oldPing.MentionedUserIds.First());
                if(user.ProjectedPercent < overridepercent) {
                    await command.Channel.SendMessageAsync($"<@{oldPing.MentionedUserIds.First()}>");
                }
                messagesToDelete.Add(oldPing);
                var discordUser = await (command.Channel as ITextChannel).Guild.GetUserAsync(user.DiscordUser.Id);

                if(usersAbovePercent.Any(y => y.DiscordUser.Id == oldPing.MentionedUserIds.First())) {
                    await (command.Channel as IThreadChannel).RemoveUserAsync(discordUser);
                }
            }


            foreach(var commandMessage in joinCommands) {
                if(commandMessage.Reference != null) {
                    var referenceMessages = threadMessages.Where(x => x.Id == commandMessage.Reference.MessageId.Value);
                    foreach(var referenceMessage in referenceMessages) {
                        await referenceMessage.DeleteAsync();
                    }
                }
                messagesToDelete.Add(commandMessage);
            }

            messagesToDelete = messagesToDelete.Distinct().ToList();
            foreach(var group in
                messagesToDelete.Where(x => x.Type != MessageType.RecipientRemove).Select((x, i) => new { Index = i, Value = x })
                    .GroupBy(x => x.Index / 20)
                    .Select(x => x.Select(v => v.Value).ToList())) {
                await thread.DeleteMessagesAsync(group);
            }


            //await command.DeleteResponseFix();
            if(usersAbovePercent.Count() > 0) {
                var usersString = string.Join("\n", usersAbovePercent.Select(x => {
                    string expireMessage = "";
                    if(x.TimeLeft < TimeSpan.Zero) {
                        expireMessage = $"Expired {x.TimeLeft.Humanize()} ago";
                    } else if(x.TimeLeft < TimeSpan.FromDays(1)) {
                        expireMessage = $"Expires in {x.TimeLeft.Humanize()}";
                    }
                    return $"{x.DBUser.DiscordUsername} {Math.Round(x.ProjectedPercent)}% {expireMessage}";
                }));
                await command.ModifyOriginalResponseAsync(x => x.Content = $"Did not add the following: \n {usersString}");
            } else {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"Pings added: {usersToPing.Count()}");
            }

        }

        [SlashCommand(Description = "Fix joining wrong co-op")]
        public static async Task FixJoinedWrongCoop(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam] string wrongcoopcode) {
            await command.RespondAsync("Attempting to fix...");



            var targetCoop = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"ERROR: Command only works in co-op channels");
                return;
            }

            if(wrongcoopcode.Equals(targetCoop.Name, StringComparison.OrdinalIgnoreCase)) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"ERROR: Please enter the wrong code you joined so we know which co-op to remove you from so you can join this one.");
                return;
            }

            await FixJoinedWrongCoopFinal(command, db, targetCoop.ContractID, wrongcoopcode, command.User.Id);
        }

        [SlashCommand(Description = "Fix joining wrong co-op", ParentCommand = "a", AdminOnly = true)]
        public static async Task FixJoinedWrongCoop(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam] string wrongcoopcode, [SlashParam] SocketGuildUser targetUser, [SlashParam] SocketChannel contractChannel) {
            await command.RespondAsync("Attempting to fix...");

            var contract = await db.GuildContracts.Where(x => x.DiscordChannelId == contractChannel.Id).Select(x => x.Contract).FirstOrDefaultAsync();
            if(contract is null) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"ERROR: Unable to find contract, is <#{contractChannel}> a contract channel?");
                return;
            }

            await FixJoinedWrongCoopFinal(command, db, contract.ID, wrongcoopcode, targetUser.Id);
        }

        private static async Task FixJoinedWrongCoopFinal(FauxCommand command, ApplicationDbContext db, string contractID, string wrongcoopcode, ulong DiscordUserID) {
            var coopStatus = await ContractsAPI.GetCoopStatus(contractID, wrongcoopcode.ToLower().Trim());
            if(coopStatus is null) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"ERROR: Unable to find co-op {wrongcoopcode}");
                return;
            }

            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == DiscordUserID);

            //var egginids = user.EggIncIds.Select(x => x.Id).ToList();

            var participant = coopStatus.Participants.FirstOrDefault(x => user.Backups.Any(y => y.UserName == x.UserName));
            if(participant is null) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"Unable to find an assigned user in co-op {wrongcoopcode}. {(coopStatus.Participants.Count > 0 ? $"Users found: \n{string.Join("\n", coopStatus.Participants.Select(x => x.UserName))}" : "")}");
                return;
            }

            var r = await ContractsAPI.Send(new Ei.KickPlayerCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = contractID,
                CoopIdentifier = wrongcoopcode,
                PlayerIdentifier = participant.UserId,
                Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                RequestingUserId = coopStatus.CreatorId
            }, participant.UserId);

            if(!r) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $"ERROR: Unable to remove user from co-op {wrongcoopcode}");
                return;
            }
            await command.ModifyOriginalResponseAsync(m => m.Content = $"<@{user.DiscordId}> should now be able to re-join co-op. Don't use this as an excuse to be lazy next time, double check your co-op codes and make sure it says **Join**");

        }
    }
}


