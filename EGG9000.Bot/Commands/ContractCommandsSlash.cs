using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

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
        public static async Task MakePublic(SocketSlashCommand command, ApplicationDbContext db) {
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
        public static async Task MakePrivate(SocketSlashCommand command, ApplicationDbContext db) {
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
        public static async Task AddPrefarmers(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }
            await command.RespondAsync($"Please wait...adding prefarmers");

            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);
            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
            //var backups = await _apiLink.GetUserBackups(dbusers, db);
            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                User = y,
                Backup = x
            })).ToList();
            var allUsers = GetPrefarmers(backups, guildContract.Contract);
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract, guild);

            foreach(var user in coopsBreakdown.Coops.SelectMany(x => x.Users)) {
                var discordUser = guild.GetUser(user.DiscordId);
                await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(discordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
            }

            foreach(var user in coopsBreakdown.ExpiredFarms) {
                var discordUser = guild.GetUser(user.DiscordId);
                await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(discordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
            }

            await command.Channel.SendMessageAsync($"{coopsBreakdown.Coops.SelectMany(x => x.Users).Count()} prefarmers added");
        }


        [SlashCommand(Description = "Adds an outside co-op so you can track it's progress", AdminOnly = true, AllowFarmHand = true)]
        public static async Task AddCoop(SocketSlashCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname) {
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
        public static async Task FixReference(SocketSlashCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser targetuser, [SlashParam(Description = "Egg Inc Name, will match partial name")] string eggincname) {
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

            var newxref = new UserCoopXref {
                AddedToChannel = true,
                CoopId = xref.CoopId,
                CreatedOn = xref.CreatedOn,
                JoinedCoop = false,
                Starter = false,
                UserId = xref.GetID(),
                WaitingOnStarter = false,
                EggIncId = t.GetID(),
                RefEggIncId = xref.EggIncId,
                WasAssigned = true
            };

            db.Remove(xref);
            db.Add(newxref);

            await command.RespondAsync($"Fixed {targetuser.Mention} reference");
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Move a user to a co-op. Tag channel or type co-op name", AdminOnly = true)]
        public static async Task MoveToCoop(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketGuildUser user, [SlashParam(Required = false)] SocketChannel coop, [SlashParam(Required = false)] string coopname, [SlashParam(Required = false)] int accountnumber) {
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
        public static async Task RemoveFromCoop(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketGuildUser user) {
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
        public static async Task DeleteContract(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            guildContract.DeletedChannel = true;
            await db.SaveChangesAsync();
            var channel = (SocketTextChannel)command.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand(Description = "Makes it so the bot won't notify you for contracts that don't have an Egg of Prophecy (PE)")]
        public static async Task SkipNoEggOfPhophecy(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync($"ERROR: Unable to find user");
                return;
            }

            dbUser.SkipNoPE = true;
            await db.SaveChangesAsync();
            await command.RespondAsync($"{command.User.Mention} is set to skip contracts without <:Egg_of_Prophecy_PE:669981330477547580>, what this means is you won't get a demerit for not participating in this contract. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server.");
        }

        [SlashCommand(Description = "Bot will notifiy you of contracts even without an Egg of Prophecy (PE)")]
        public static async Task UnSkipNoPeEggOfPhophecy(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync($"ERROR: Unable to find user");
                return;
            }

            dbUser.SkipNoPE = false;
            await db.SaveChangesAsync();
            await command.RespondAsync($"{command.User.Mention} is NO longer set to skip contracts without an <:Egg_of_Prophecy_PE:669981330477547580>.");
        }

        [SlashCommand(Description = "Stop the bot from pinging you for the tagged contract.")]
        public static async Task Skip(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }

            var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");
            skipList.Add(command.User.Id);

            guildContract.Skip = JsonConvert.SerializeObject(skipList);
            await db.SaveChangesAsync();
            await command.RespondAsync($"{command.User.Mention} is set to skip {((SocketTextChannel)contractchannel).Mention}. **If you have already started the contract and don't exit it, you will still get a demerit**. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server. Did you know there is a new command !skipNoPe that will allow you to automatically skip all contracts without an <:Egg_of_Prophecy_PE:669981330477547580>?");
        }


        private static async Task _start(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, int percent, APILink _apiLink, Words _words) {
            var coopCount = 0;

            await command.RespondAsync($"Working...");
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                return;
            }


            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
            //var dbusers = await db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

            //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
            //var backups = await _apiLink.GetUserBackups(dbusers, db);
            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                User = y,
                Backup = x
            })).ToList();

            var allUsers = GetPrefarmers(backups, guildContract.Contract);
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract, guild);



            foreach(var coopDetail in coopsBreakdown.Coops) {
                var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
                var cooppercent = (decimal)(coopDetail.Users.Sum(x => x.Projected) / targetAmount) * 100m;
                if(cooppercent < percent) {
                    continue;
                }


                var coop = await CreateCoops.Start(coopDetail.Users, guildContract, guild, _words, db);


                coopCount++;
            }

            if(percent == 0 || coopCount == coopsBreakdown.Coops.Count) {
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
        public static async Task StartUser(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] SocketGuildUser user, [SlashParam(Description = "Fill the co-op with other users")] bool fillcoop) {
            await _StartUser(command, db, _client, _apiLink, _words, fillcoop, user);
        }


        [SlashCommand(Description = "Start co-ops with users above a certain percent and backfill with low EB users", AdminOnly = true)]
        public static async Task StartFill(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam(Description = "Percent at which a user will be started")] int percenttostart) {
            await _StartUser(command, db, _client, _apiLink, _words, true, percent: percenttostart);
        }


        public static async Task _StartUser(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, bool fill, SocketGuildUser user = null, int? percent = null) {
            var coopCount = 0;

            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
                return;
            }

            await command.RespondAsync($"Working...");


            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
            //var dbusers = await db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

            //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
            //var backups = await _apiLink.GetUserBackups(dbusers, db);

            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                User = y,
                Backup = x
            })).ToList();


            var allUsers = GetPrefarmers(backups, guildContract.Contract);
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract, guild);

            var prefarms = coopsBreakdown.Coops.SelectMany(x => x.Users).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;


            if(user == null) {
                var usersAbovePercent = prefarms.Where(p => (p.Projected / targetAmount) * 100 >= percent).ToList();
                usersAbovePercent.ForEach(x => prefarms.Remove(x));
                prefarms = prefarms.Where(p => (p.Projected / targetAmount) * 100 < 5).ToList();
                foreach(var userAbovePercent in usersAbovePercent) {
                    var participants = await _startUserCreateCoop(userAbovePercent, guildContract, prefarms, guild, db, _words);
                    participants.ForEach(x => prefarms.Remove(x));
                    coopCount++;
                }
            } else {
                var prefarm = prefarms.First(x => x.DiscordId == user.Id);
                prefarms.Remove(prefarm);
                _ = await _startUserCreateCoop(prefarm, guildContract, prefarms, guild, db, _words, fill);
                coopCount++;
            }

            guildContract.NumberOfCoops = Math.Max(0, guildContract.NumberOfCoops - coopCount);
            await db.SaveChangesAsync();

            try {
                await command.Channel.SendMessageAsync($"Finished Starting {coopCount} coop{(coopCount > 1 ? "s" : "")}");
            } catch(Exception) {
                //Possible message was deleted in the meantime
            }
        }

        private static async Task<List<UserPreFarm>> _startUserCreateCoop(UserPreFarm user, GuildContract guildContract, List<UserPreFarm> prefarms, SocketGuild guild, ApplicationDbContext db, Words _words, bool empty = false) {
            var participants = new List<UserPreFarm>();
            if(guildContract.Contract.MaxUsers > 4) {
                participants.AddRange(prefarms.Where(x => x.DiscordId == user.DiscordId));
            }
            participants.Add(user);
            if(!empty) {
                participants.AddRange(prefarms.TakeLast(guildContract.Contract.MaxUsers - participants.Count));
            }


            var coop = await CreateCoops.Start(participants, guildContract, guild, _words, db);
            return participants;
        }


        [SlashCommand(Description = "Start an empty co-op", AdminOnly = true)]
        public static async Task StartEmpty(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"Unable to find contract, is this run in a contract channel?");
                return;
            }
            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

            var coop = await CreateCoops.Start(new List<UserPreFarm>(), guildContract, guild, _words, db);
            await db.SaveChangesAsync();
            await command.RespondAsync($"Empty co-op created");
            return;
        }

        [SlashCommand(Description = "Start all co-ops as-is that are above a certain percent", AdminOnly = true)]
        public static async Task StartPercent(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] int percent) {
            await _start(command, db, _client, percent, _apiLink, _words);
        }

        [SlashCommand(Description = "Start all co-ops", AdminOnly = true)]
        public static async Task StartAll(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
            await _start(command, db, _client, 0, _apiLink, _words);
        }


        //public static async Task SetNumber(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
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
        public static async Task SetNumber(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(Description = "Number of potental co-ops (excludes ones already created)")] int numberofcoops) {
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

        //public static async Task Update(SocketSlashCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    await command.RespondAsync($"Updating...");
        //    ContractUpdater.ResetTimeStatic();
        //}

        private static readonly AsyncLock joinLock = new AsyncLock();
        [SlashCommand(Description = "Join a Co-op (Only useable in a #spots thread)")]
        public static async Task Join(SocketSlashCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client) {
            await command.RespondAsync("Please wait finding a co-op...");
            using(await joinLock.LockAsync()) {
                var discordUser = command.User;
                var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == discordUser.Id);
                var thread = (SocketThreadChannel)command.Channel;
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
                var prefarmers = GetPrefarmers(backups, guildContract.Contract);

                var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
                var alienBackups = await GetBackupsForAliens(coops, prefarmers, guildContract.Contract, _apiLink);
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

                foreach(var coop in currentCoops) {
                    var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
                    if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize) {
                        targetCoop = coop.Coop;
                        break;
                    }

                }
                if(targetCoop == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to find open co-op, all spots may have been filled.");
                    return;

                }

                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == dbuser.DiscordId).ToListAsync();
                if(xrefs.Count == dbuser.EggIncIds.Count && xrefs.First().JoinedCoop) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: <@{dbuser.DiscordId}> has already joined {xrefs.First().Coop.Name}");
                    return;
                }

                string EggIncId;
                var eggIncName = "";
                if(xrefs.Count == 0 || dbuser.EggIncIds.Count > 1) {
                    if(dbuser.EggIncIds.Count > 1) {
                        var contract = await db.Contracts.AsQueryable().FirstAsync(x => x.ID == targetCoop.ContractID);
                        var prefarms = prefarmers.Where(x => x.DatabaseId == dbuser.Id).ToList();

                        EggIncId = prefarms.First().EggIncId;
                        eggIncName = $" ({prefarms.First().Backup.UserName})";
                    } else {
                        EggIncId = dbuser.EggIncIds.First().Id;
                    }
                } else {
                    EggIncId = xrefs.First().EggIncId;
                }

                var channel = (SocketTextChannel)_client.GetChannel(targetCoop.DiscordChannelId);
                var newxref = await CreateCoops.MoveUser(targetCoop, dbuser.Id, EggIncId, eggIncName, discordUser, dbuser, (SocketTextChannel)channel, (SocketTextChannel)command.Channel);

                if(newxref == null) {
                    await command.ModifyOriginalResponseAsync(x => x.Content = $"ERROR: Unable to add permission for {discordUser.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                    return;
                }

                db.RemoveRange(xrefs);
                db.Add(newxref);

                await db.SaveChangesAsync();
                await command.ModifyOriginalResponseAsync(x => x.Content = $"Moved you to a co-op, the bot should have pinged you in that co-op.");
            }
        }


        [SlashCommand(Description = "Ping people to add them to a spots thread", AdminOnly = true)]
        public static async Task SpotPings(SocketSlashCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client) {
            var discordUser = command.User;
            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == discordUser.Id);
            var thread = (SocketThreadChannel)command.Channel;
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
            if(guildContract == null) {
                await command.RespondAsync($"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
                return;
            }
            await command.RespondAsync("Pinging users and removing existing pings that aren't needed", ephemeral: true);
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
            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);
            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                User = y,
                Backup = x
            })).ToList();

            var allUsers = GetPrefarmers(backups, guildContract.Contract);
            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract, guild);

            var threadMessages = await command.Channel.GetMessagesAsync(limit: 500).FlattenAsync();



            var usersWithFarm = new List<UserPreFarm>();
            usersWithFarm.AddRange(coopsBreakdown.Coops.SelectMany(x => x.Users));
            usersWithFarm.AddRange(coopsBreakdown.ExpiredFarms);


            var usersToPing = usersWithFarm.Where(u => !threadMessages.Any(m => m.MentionedUserIds.Any(mu => mu == u.DiscordId))).ToList();
            var pingsToRemove = threadMessages.Where(m => 
             (m.MentionedUserIds.Count == 1 && !usersWithFarm.Any(u => u.DiscordId == m.MentionedUserIds.First()))
             //|| (!usersWithFarm.Any(u => m.Author.Id == u.DiscordId) && !m.Author.IsBot && !m.Author.rol)
              
            ).ToList();


            foreach(var user in usersToPing) {
                await command.Channel.SendMessageAsync($"<@{user.DiscordId}>");
            }

            foreach(var ping in pingsToRemove) {
                await ping.DeleteAsync();
            }

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Pings added: {usersToPing.Count()} Ping Removed: {pingsToRemove.Count()}");

        }
    }
}


