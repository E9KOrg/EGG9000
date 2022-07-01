//using Discord;
//using Discord.WebSocket;

//using EGG9000.Bot.Automated;
//using EGG9000.Bot.EggIncAPI;
//using EGG9000.Bot.Helpers;
//using EGG9000.Bot.Services;
//using EGG9000.Common.Database;
//using EGG9000.Common.Database.Entities;
//using EGG9000.Common.Helpers;

//using Microsoft.EntityFrameworkCore;

//using Newtonsoft.Json;

//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text.RegularExpressions;
//using System.Threading.Tasks;

//using static EGG9000.Common.Helpers.Prefarm;

//namespace EGG9000.Bot.Commands
//{
//    public static class ContractCommands
//    {
//        public static async Task MakePublic(SocketMessage message, ApplicationDbContext db)
//        {
//            var name = new Regex(@"\w+").Match(message.Channel.Name.ToLower()).Value;
//            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == message.Channel.Id);
//            if(coop == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find coop for this channel {message.Channel.Name}");
//                return;
//            }

//            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest
//            {
//                ClientVersion = 30,
//                ContractIdentifier = coop.ContractID,
//                CoopIdentifier = coop.Name.ToLower(),
//                Public = true,
//                RequestingUserId = ContractsAPI.UserId
//            }, ContractsAPI.UserId);

//            if(response.Success)
//            {
//                await message.Channel.SendMessageAsync($"{coop.Name} is now public.");
//            } else
//            {
//                await message.Channel.SendMessageAsync($"{coop.Name} should now be public.");
//                //await message.Channel.SendMessageAsync($"ERROR: {response.Message}");
//            }
//        }
//        public static async Task MakePrivate(SocketMessage message, ApplicationDbContext db)
//        {
//            var name = new Regex(@"\w+").Match(message.Channel.Name.ToLower()).Value;
//            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == message.Channel.Id);
//            if(coop == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find coop for this channel {message.Channel.Name}");
//                return;
//            }

//            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest
//            {
//                ClientVersion = 30,
//                ContractIdentifier = coop.ContractID,
//                CoopIdentifier = coop.Name.ToLower(),
//                Public = false,
//                RequestingUserId = ContractsAPI.UserId
//            }, ContractsAPI.UserId);

//            if(response.Success)
//            {
//                await message.Channel.SendMessageAsync($"{coop.Name} is now private.");
//            } else
//            {
//                await message.Channel.SendMessageAsync($"{coop.Name} should now be private.");
//                //await message.Channel.SendMessageAsync($"ERROR: {response.Message}");
//            }
//        }

//        public static async Task AddPrefarmers(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink)
//        {
//            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
//            var targetChannel = message.MentionedChannels.FirstOrDefault();
//            if(targetChannel == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, please tag the channel of the contract you want to skip");
//                return;
//            }
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == targetChannel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
//                return;
//            }
//            await message.Channel.SendMessageAsync($"Please wait...adding prefarmers");

//            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);
//            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
//            //var backups = await _apiLink.GetUserBackups(dbusers, db);
//            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser
//            {
//                User = y,
//                Backup = x
//            })).ToList();
//            var allUsers = GetPrefarmers(backups, guildContract);
//            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
//            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
//            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
//            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
//            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
//            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract);

//            foreach(var user in coopsBreakdown.Coops.SelectMany(x => x.Users))
//            {
//                var discordUser = guild.GetUser(user.DiscordId);
//                await ((ITextChannel)message.Channel).AddPermissionOverwriteAsync(discordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
//            }

//            foreach(var user in coopsBreakdown.ExpiredFarms)
//            {
//                var discordUser = guild.GetUser(user.DiscordId);
//                await ((ITextChannel)message.Channel).AddPermissionOverwriteAsync(discordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
//            }

//            await message.Channel.SendMessageAsync($"{coopsBreakdown.Coops.SelectMany(x => x.Users).Count()} prefarmers added");
//        }

//        public static async Task RemoveCoop(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            Console.WriteLine("Removing Coop");
//            if(!((SocketGuildUser)message.Author).Roles.Any(x => x.Name.ToLower() == "Admin") && message.Author.Username != "kendrome")
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Missing admin permissions");
//                return;
//            }

//            Coop coop;
//            ITextChannel channel;
//            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));

//            if(args.Length > 0)
//            {
//                coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.Name.ToLower() == args[0].ToLower());
//                if(coop == null)
//                {
//                    await message.Channel.SendMessageAsync($"ERROR: Unable to find a coop with the name {args[0]}");
//                    return;
//                }
//                channel = guild.TextChannels.FirstOrDefault(x => x.Name.ToLower() == coop.Name.ToLower());
//                Console.WriteLine($"Found coop from name {args[0]}");
//            } else
//            {
//                coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.Name.ToLower() == message.Channel.Name.ToLower());
//                if(coop == null)
//                {
//                    await message.Channel.SendMessageAsync($"ERROR: Unable to find a coop with the channel name {message.Channel.Name}");
//                    return;
//                }
//                channel = (ITextChannel)message.Channel;
//                Console.WriteLine($"Found coop from channel {message.Channel.Name}");
//            }

//            if(channel != null)
//            {
//                await channel.DeleteAsync();
//            }


//            db.Coops.Remove(coop);
//            await db.SaveChangesAsync();
//            Console.WriteLine("Completed Removing Coop");
//            Console.WriteLine("Finished Removing Coop");
//        }


//        public static async Task AddCoop(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink)
//        {
//            var coopName = args[0];

//            var targetChannel = message.MentionedChannels.FirstOrDefault();
//            if(targetChannel == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, please tag the channel of the contract you want to skip");
//                return;
//            }
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == targetChannel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
//                return;
//            }

//            var status = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coopName.ToLower());
//            if(status != null && status.Success)
//            {

//                var coop = new Coop
//                {
//                    ContractID = guildContract.ContractID,
//                    Created = DateTimeOffset.Now,
//                    GuildId = guildContract.GuildID,
//                    Name = coopName,
//                    MaxUsers = guildContract.Contract.MaxUsers,
//                    Status = CoopStatusEnum.WaitingOnAssigned,
//                    League = (UInt32)(guildContract.Elite ? 0 : 1),
//                    CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
//                };
//                db.Coops.Add(coop);
//                await db.SaveChangesAsync();
//                await message.Channel.SendMessageAsync($"Co-op Added: {coopName} for {((SocketTextChannel)targetChannel).Mention}");
//                return;
//            } else
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find co-op details, double check co-op name ({coopName}) and correct contract channel ({((SocketTextChannel)targetChannel).Mention}).");
//                return;
//            }
//        }




//        public static async Task FixReference(SocketMessage message, string[] args, ApplicationDbContext db)
//        {
//            var user = message.MentionedUsers.First();
//            var eggincName = args[1];
//            //var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == message.Channel.Id);
//            var xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == user.Id && x.Coop.DiscordChannelId == message.Channel.Id && !x.JoinedCoop);
//            if(xref == null)
//            {
//                xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == user.Id && x.Coop.DiscordChannelId == message.Channel.Id);
//            }
//            if(xref == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Bot error - Unable to find user assignment to co-op\nex: !fixalien @user EggIncName");
//                return;
//            }


//            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(eggincName.ToLower()));
//            if(t == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Bot error - Unable to find user in co-op. You can use a partial in-game name.\nex: !fixalien @user EggIncName");
//                return;
//            }



//            var newxref = new UserCoopXref
//            {
//                AddedToChannel = true,
//                CoopId = xref.CoopId,
//                CreatedOn = xref.CreatedOn,
//                JoinedCoop = false,
//                Starter = false,
//                UserId = xref.GetID(),
//                WaitingOnStarter = false,
//                EggIncId = t.GetID(),
//                RefEggIncId = xref.EggIncId,
//                WasAssigned = true
//            };


//            db.Remove(xref);
//            db.Add(newxref);

//            await message.Channel.SendMessageAsync($"Fixed {user.Mention} reference");
//            await db.SaveChangesAsync();
//        }

//        public static async Task Move(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            var channel = message.MentionedChannels.FirstOrDefault();
//            var user = message.MentionedUsers.First();
//            Coop targetCoop;
//            if(channel == null)
//            {
//                var coopname = args[1].Contains("@") ? args[0] : args[1];
//                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.Name.ToLower() == coopname.ToLower());
//                if(targetCoop == null)
//                {
//                    await message.Channel.SendMessageAsync($"ERROR: Unable to find co-op name {coopname}");
//                    return;
//                }
//                var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;
//                channel = _client.Guilds.First(x => x.Id == guildId).GetChannel(targetCoop.DiscordChannelId);
//            } else
//            {
//                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.DiscordChannelId == channel.Id);
//            }
//            //var contract = await db.GuildContracts.Include(x => x.Contract).ThenInclude(x => x.Coops).FirstAsync(x => x.ContractID == targetCoop.ContractID);

//            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == user.Id);



//            var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == user.Id).ToListAsync();
//            if(xrefs.Count == dbuser.EggIncIds.Count && xrefs.First().JoinedCoop)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: {user.Mention} has already joined {xrefs.First().Coop.Name}");
//                return;
//            }

//            Guid dbuserid;
//            string EggIncId;
//            var eggIncName = "";
//            if(xrefs.Count == 0 || dbuser.EggIncIds.Count > 1)
//            {
//                dbuserid = dbuser.Id;
//                if(dbuser.EggIncIds.Count > 1)
//                {
//                    var contract = await db.Contracts.AsQueryable().FirstAsync(x => x.ID == targetCoop.ContractID);
//                    var prefarms = dbuser.Backups.Select(b => Prefarm.BackupToPreFarm(new LeaderboardUser { Backup = b, User = dbuser }, contract)).Where(x => x.Elite ? targetCoop.League == 0 : targetCoop.League == 1).ToList();

//                    prefarms = prefarms.Where(x => !xrefs.Any(y => y.EggIncId == x.EggIncId)).ToList();
//                    if(prefarms.Count == 1)
//                    {
//                        EggIncId = prefarms.First().EggIncId;
//                        eggIncName = $" ({prefarms.First().Backup.UserName})";
//                    } else if(args.Count() < 3)
//                    {
//                        var count = 1;
//                        await message.Channel.SendMessageAsync($"User has multiple accounts, please specifiy which account `!move @user #channel accountnumber`\n{String.Join("\n", prefarms.Select(x => $"{count++} {x.Backup.UserName} Projected: {x.Projected.ToEggString()}"))}");
//                        return;
//                    } else
//                    {
//                        EggIncId = prefarms[int.Parse(args[2]) - 1].EggIncId;
//                        eggIncName = $" ({prefarms[int.Parse(args[2]) - 1].Backup.UserName})";
//                    }
//                    xrefs.Clear();
//                } else
//                {
//                    EggIncId = dbuser.EggIncIds.First().Id;
//                }
//            } else
//            {
//                dbuserid = xrefs.First().GetID();
//                EggIncId = xrefs.First().EggIncId;
//            }

//            var newxref = await CreateCoops.MoveUser(targetCoop, dbuserid, EggIncId, eggIncName, user, dbuser, (SocketTextChannel)channel, (SocketTextChannel)message.Channel);

//            if(newxref == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to add permission for {user.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
//                return;
//            }

//            db.RemoveRange(xrefs);
//            db.Add(newxref);

//            await message.Channel.SendMessageAsync($"Moved {user.Mention}{eggIncName} to {((ITextChannel)channel).Mention}");
//            await db.SaveChangesAsync();
//        }

//        public static async Task Remove(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            var user = message.MentionedUsers.FirstOrDefault();
//            UserCoopXref xref;
//            var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == message.Channel.Id);
//            if(user == null)
//            {
//                xref = await db.UserCoopXrefs.Include(x => x.User).AsQueryable().Where(xref => xref.User.DiscordUsername.Contains(args[0]) && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
//            } else
//            {
//                xref = await db.UserCoopXrefs.AsQueryable().Where(xref => xref.User.DiscordId == user.Id && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

//            }

//            if(xref == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unabled to find user");
//                return;
//            }

//            db.Remove(xref);
//            await db.SaveChangesAsync();

//            await message.Channel.SendMessageAsync($"Removed {user?.Mention ?? xref.User.DiscordUsername} from co-op");

//        }

//        public static async Task Delete(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//            guildContract.DeletedChannel = true;
//            await db.SaveChangesAsync();
//            var channel = (SocketTextChannel)message.Channel;
//            await channel.DeleteAsync();
//        }

//        public static async Task SkipNoPe(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
//            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == socketUser.Id);
//            if(dbUser == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find user");
//                return;
//            }

//            dbUser.SkipNoPE = true;
//            await db.SaveChangesAsync();
//            await message.Channel.SendMessageAsync($"{socketUser.Mention} is set to skip contracts without <:Egg_of_Prophecy_PE:669981330477547580>, what this means is you won't get a demerit for not participating in this contract. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server.");
//        }

//        public static async Task UnSkipNoPe(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
//            var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == socketUser.Id);
//            if(dbUser == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find user");
//                return;
//            }

//            dbUser.SkipNoPE = false;
//            await db.SaveChangesAsync();
//            await message.Channel.SendMessageAsync($"{socketUser.Mention} is NO longer set to skip contracts without an <:Egg_of_Prophecy_PE:669981330477547580>.");
//        }

//        public static async Task Skip(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
//            var targetChannel = message.MentionedChannels.FirstOrDefault();
//            if(targetChannel == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, please tag the channel of the contract you want to skip");
//                return;
//            }
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == targetChannel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, have you tagged a contract channel?");
//                return;
//            }

//            var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");
//            skipList.Add(socketUser.Id);

//            guildContract.Skip = JsonConvert.SerializeObject(skipList);
//            await db.SaveChangesAsync();
//            await message.Channel.SendMessageAsync($"{socketUser.Mention} is set to skip {((SocketTextChannel)targetChannel).Mention}. **If you have already started the contract and don't exit it, you will still get a demerit**. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server. Did you know there is a new command !skipNoPe that will allow you to automatically skip all contracts without an <:Egg_of_Prophecy_PE:669981330477547580>?");
//        }


//        private static async Task _start(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, decimal percent, APILink _apiLink, Words _words)
//        {
//            var coopCount = 0;

//            await message.Channel.SendMessageAsync($"Working...");
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
//                return;
//            }


//            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

//            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
//            //var dbusers = await db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

//            //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
//            //var backups = await _apiLink.GetUserBackups(dbusers, db);
//            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser
//            {
//                User = y,
//                Backup = x
//            })).ToList();

//            var allUsers = GetPrefarmers(backups, guildContract);
//            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
//            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
//            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
//            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
//            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
//            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract);



//            foreach(var coopDetail in coopsBreakdown.Coops)
//            {
//                var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
//                var cooppercent = (decimal)(coopDetail.Users.Sum(x => x.Projected) / targetAmount) * 100m;
//                if(cooppercent < percent)
//                {
//                    continue;
//                }


//                var coop = await CreateCoops.Start(coopDetail.Users, guildContract, guild, _words, db);


//                coopCount++;
//            }

//            if(percent == 0 || coopCount == coopsBreakdown.Coops.Count)
//            {
//                guildContract.Status = ContractStatus.Creating;
//                //await ((SocketGuildChannel)message.Channel).ModifyAsync(x => x.Name = "📥" + guildContract.ContractID);
//            } else
//            {
//                //await ((SocketGuildChannel)message.Channel).ModifyAsync(x => x.Name = "🐣📥" + guildContract.ContractID);
//            }


//            guildContract.NumberOfCoops = Math.Max(0, guildContract.NumberOfCoops - coopCount);
//            await db.SaveChangesAsync();
//        }

//        public static async Task StartUser(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, bool fill)
//        {
//            var coopCount = 0;

//            var workingMessage = await message.Channel.SendMessageAsync($"Working...");
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
//                return;
//            }


//            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

//            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
//            //var dbusers = await db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

//            //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
//            //var backups = await _apiLink.GetUserBackups(dbusers, db);

//            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser
//            {
//                User = y,
//                Backup = x
//            })).ToList();


//            var allUsers = GetPrefarmers(backups, guildContract);
//            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
//            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
//            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
//            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
//            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
//            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract);

//            var prefarms = coopsBreakdown.Coops.SelectMany(x => x.Users).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

//            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;


//            if(fill)
//            {
//                if(double.TryParse(args[0], out var percent))
//                {
//                    var usersAbovePercent = prefarms.Where(p => (p.Projected / targetAmount) * 100 >= percent).ToList();
//                    usersAbovePercent.ForEach(x => prefarms.Remove(x));
//                    prefarms = prefarms.Where(p => (p.Projected / targetAmount) * 100 < 5).ToList();
//                    foreach(var user in usersAbovePercent)
//                    {
//                        var participants = await _startUserCreateCoop(user, guildContract, prefarms, guild, db, _words, args.Any(x => x == "empty"));
//                        participants.ForEach(x => prefarms.Remove(x));
//                        coopCount++;
//                    }
//                } else
//                {
//                    await message.Channel.SendMessageAsync($"ERROR: Missing percent, ex. !startfill 100");
//                    return;
//                }
//            } else
//            {
//                var user = prefarms.First(x => x.DiscordId == message.MentionedUsers.First().Id);
//                prefarms.Remove(user);
//                _ = await _startUserCreateCoop(user, guildContract, prefarms, guild, db, _words, args.Any(x => x == "empty"));
//                coopCount++;
//            }

//            guildContract.NumberOfCoops = Math.Max(0, guildContract.NumberOfCoops - coopCount);
//            await db.SaveChangesAsync();

//            try
//            {
//                await workingMessage.ModifyAsync(m => m.Content = $"Finished Starting {coopCount} coop{(coopCount > 1 ? "s" : "")}");
//            } catch(Exception)
//            {
//                //Possible message was deleted in the meantime
//            }
//        }

//        private static async Task<List<UserPreFarm>> _startUserCreateCoop(UserPreFarm user, GuildContract guildContract, List<UserPreFarm> prefarms, SocketGuild guild, ApplicationDbContext db, Words _words, bool empty = false)
//        {
//            var participants = new List<UserPreFarm>();
//            if(guildContract.Contract.MaxUsers > 4)
//            {
//                participants.AddRange(prefarms.Where(x => x.DiscordId == user.DiscordId));
//            }
//            participants.Add(user);
//            if(!empty)
//            {
//                participants.AddRange(prefarms.TakeLast(guildContract.Contract.MaxUsers - participants.Count));
//            }


//            var coop = await CreateCoops.Start(participants, guildContract, guild, _words, db);
//            return participants;
//        }

//        public static async Task Fill(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink)
//        {
//            var workingMessage = await message.Channel.SendMessageAsync($"Working...");
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
//                return;
//            }
//            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

//            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guild.Id).ToListAsync();
//            //var dbusers = await db.Users.Where(x => x.GuildId == guild.Id).ToListAsync();

//            //var backups = await ContractsAPI.GetUserBackups(_cache, dbusers);
//            //var backups = await _apiLink.GetUserBackups(dbusers, db);
//            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser
//            {
//                User = y,
//                Backup = x
//            })).ToList();



//            var allUsers = GetPrefarmers(backups, guildContract);
//            var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildContract.GuildID);
//            var inactiveUsers = JsonConvert.DeserializeObject<List<GuildUser>>(guildContract.Elite ? dbguild.InactiveElites : dbguild.InactiveStandards);
//            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
//            var users = allUsers.Where(x => x.Elite == guildContract.Elite && (x.NumChickens > 0 || !inactiveUsers.Any(y => y.DatabaseId == x.DatabaseId))).ToList();
//            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
//            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract);

//            var prefarms = coopsBreakdown.Coops.SelectMany(x => x.Users).OrderByDescending(x => x.Backup.EarningsBonus);

//            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;

//            var channel = message.MentionedChannels.FirstOrDefault();
//            Coop targetCoop;
//            if(channel == null)
//            {
//                var coopname = args[1].Contains("@") ? args[0] : args[1];
//                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.Name.ToLower() == coopname.ToLower());
//                if(targetCoop == null)
//                {
//                    await message.Channel.SendMessageAsync($"ERROR: Unable to find co-op name {coopname}");
//                    return;
//                }
//                var guildId = targetCoop.OverflowGuildId > 0 ? targetCoop.OverflowGuildId : targetCoop.GuildId;
//                channel = _client.Guilds.First(x => x.Id == guildId).GetChannel(targetCoop.DiscordChannelId);
//            } else
//            {
//                targetCoop = await db.Coops.AsQueryable().Include(x => x.Contract).FirstAsync(x => x.DiscordChannelId == channel.Id);
//            }

//            var participants = prefarms.TakeLast(guildContract.Contract.MaxUsers - targetCoop.UserCoopsXrefs.Count);
//            var xrefs = new List<UserCoopXref>();
//            foreach(var participant in participants)
//            {
//                var newxref = await CreateCoops.MoveUser(targetCoop, participant.DatabaseId.Value, participant.EggIncId, participant.User.EggIncIds.First(x => x.Id == participant.EggIncId).Name, participant.DiscordUser, participant.User, (SocketTextChannel)channel, (SocketTextChannel)message.Channel);
//                db.Add(newxref);
//                xrefs.Add(newxref);
//            }
//            await message.Channel.SendMessageAsync($"Moved {string.Join(", ", participants.Select(x => x.DiscordUser.Mention))} to {((ITextChannel)channel).Mention}");
//            await db.SaveChangesAsync();

//        }

//        public static async Task StartEmpty(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words)
//        {
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"Unable to find contract, is this run in a contract channel?");
//                return;
//            }
//            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

//            var coop = await CreateCoops.Start(new List<UserPreFarm>(), guildContract, guild, _words, db);
//            await db.SaveChangesAsync();
//            await message.Channel.SendMessageAsync($"Empty co-op created");
//            return;
//        }
//        public static async Task StartPercent(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words)
//        {
//            if(decimal.TryParse(args[0], out var percent))
//            {
//                await _start(message, args, db, _client, percent, _apiLink, _words);
//                return;
//            }
//        }

//        public static async Task StartAll(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words)
//        {
//            await _start(message, args, db, _client, 0, _apiLink, _words);
//        }


//        //public static async Task SetNumber(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
//        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == message.Channel.Id);
//        //    if(guildContract == null) {
//        //        await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract channel?");
//        //        return;

//        //    }

//        //    if(args.Length == 0) {
//        //        await message.Channel.SendMessageAsync($"ERROR: Please include the number of coops you would like the bot to create");
//        //        return;
//        //    }

//        //    var number = Int32.Parse(args[0]);

//        //    guildContract.NumberOfCoops = number;
//        //    await db.SaveChangesAsync();
//        //    await message.Channel.SendMessageAsync($"# of coops set to {number}");
//        //}


//        public static async Task Update(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client)
//        {
//            await message.Channel.SendMessageAsync($"Updating...");
//            ContractUpdater.ResetTimeStatic();
//        }

//        public static async Task Join(SocketMessage message, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client)
//        {
//            var discordUser = message.MentionedUsers.FirstOrDefault() ?? message.Author;
//            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == discordUser.Id);
//            SocketThreadChannel thread;
//            try {
//                thread = (SocketThreadChannel)message.Channel;
//            }catch(AggregateException) {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
//                return;
//            }
//            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
//            if(guildContract == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
//                return;
//            }
//            var targetAmount = guildContract.Contract.Details.GoalSets[guildContract.Elite ? 0 : 1].Goals.Last().TargetAmount;
//            var rawusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildContract.GuildID).Select(x => new
//            {
//                x.DiscordId,
//                x.DiscordUsername,
//                x.GuildId,
//                x.Id,
//                x._CustomBackups,
//                x._eggIncIds,
//                x.TempDisabled
//            }).ToListAsync();
//            var dbusers = rawusers.Select(x => new DBUser { TempDisabled = x.TempDisabled, DiscordId = x.DiscordId, DiscordUsername = x.DiscordUsername, GuildId = x.GuildId, Id = x.Id, _CustomBackups = x._CustomBackups, _eggIncIds = x._eggIncIds });
//            var backups = dbusers.Where(x => x.Backups != null).SelectMany(y => y.Backups.Select(x => new LeaderboardUser
//            {
//                User = y,
//                Backup = x
//            })).ToList();
//            var prefarmers = GetPrefarmers(backups, guildContract);

//            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
//            var alienBackups = await GetBackupsForAliens(coops, prefarmers, guildContract.Contract, _apiLink);
//            var currentCoops = coops.Select(coop =>
//            {
//                var prefarms = GetPrefarmsForCoop(coop, prefarmers, alienBackups, guildContract.Contract);
//                return new
//                {
//                    Coop = coop,
//                    Prefarms = prefarms,
//                    TimeRemaining = GetTimeRemainingValue(targetAmount, prefarms.Sum(x => x.Rate), prefarms.Sum(x => x.EggsPaidFor + x.Rate * x.TimeSinceUpdate.TotalSeconds)),
//                    HasSpots = guildContract.Contract.Details.MaxCoopSize > prefarms.Count
//                };
//            }).Where(x => !x.Coop.Finished && x.HasSpots).OrderByDescending(x => x.HasSpots).ThenBy(x => x.TimeRemaining).ToList();

//            if(currentCoops.Count == 0)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find open co-op, all spots may have been filled.");
//                return;
//            }


//            Coop targetCoop = null;

//            foreach(var coop in currentCoops)
//            {
//                var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
//                if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize)
//                {
//                    targetCoop = coop.Coop;
//                    break;
//                }

//            }
//            if(targetCoop == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to find open co-op, all spots may have been filled.");
//                return;

//            }

//            var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == dbuser.DiscordId).ToListAsync();
//            if(xrefs.Count == dbuser.EggIncIds.Count && xrefs.First().JoinedCoop)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: <@{dbuser.DiscordId}> has already joined {xrefs.First().Coop.Name}");
//                return;
//            }

//            string EggIncId;
//            var eggIncName = "";
//            if(xrefs.Count == 0 || dbuser.EggIncIds.Count > 1)
//            {
//                if(dbuser.EggIncIds.Count > 1)
//                {
//                    var contract = await db.Contracts.AsQueryable().FirstAsync(x => x.ID == targetCoop.ContractID);
//                    var prefarms = prefarmers.Where(x => x.DatabaseId == dbuser.Id).ToList();

//                    EggIncId = prefarms.First().EggIncId;
//                    eggIncName = $" ({prefarms.First().Backup.UserName})";
//                } else
//                {
//                    EggIncId = dbuser.EggIncIds.First().Id;
//                }
//            } else
//            {
//                EggIncId = xrefs.First().EggIncId;
//            }

//            var channel = (SocketTextChannel)_client.GetChannel(targetCoop.DiscordChannelId);
//            var newxref = await CreateCoops.MoveUser(targetCoop, dbuser.Id, EggIncId, eggIncName, discordUser, dbuser, (SocketTextChannel)channel, (SocketTextChannel)message.Channel);

//            if(newxref == null)
//            {
//                await message.Channel.SendMessageAsync($"ERROR: Unable to add permission for {discordUser.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
//                return;
//            }

//            db.RemoveRange(xrefs);
//            db.Add(newxref);

//            await message.Channel.SendMessageAsync($"Moved {discordUser.Mention}{eggIncName} to {((ITextChannel)channel).Mention}");
//            await db.SaveChangesAsync();
//        }
//    }
//}


