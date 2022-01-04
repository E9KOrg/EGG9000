using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Bot.Services;


using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;

namespace EGG9000.Bot.Commands {
    public static class RegisterCommands {
        public static async Task UserJoined(SocketGuildUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;

            var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == user.Guild.Id);
            if(dbguild == null) {
                return;
            }



            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

            if(dbuser != null && dbuser.GuildId == user.Guild.Id) {



                await user.Guild.SendToGeneralChannel($"Welcome back {user.Mention}!");
                await CleanWelcomeChannel(user.Guild, user);
            } else {
                var welcomeChannel = user.Guild.GetWelcomeChannel();
                var rulesChannel = user.Guild.GetRulesChannel();
                var msg = $"Welcome to the server {user.Mention}! Please read {rulesChannel.Mention} and then send the message __**!accept**__ when you are ready.";
                var talkChannel = user.Guild.TextChannels.FirstOrDefault(x => x.Id == 746509501271769210);
                if(talkChannel != null)
                    msg += $" If you have any questions feel free to ask us in {talkChannel.Mention}, we are glad you are here!";
                await user.Guild.GetWelcomeChannel().SendMessageAsync(msg);
            }
        }

        public static async Task UserLeft(SocketGuild guild, SocketUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

            if(dbuser != null) {
                dbuser.AcceptedRules = false;
                dbuser.GuildId = 0;
                await db.SaveChangesAsync();
            }
            await CleanWelcomeChannel(guild, user);
        }

        public static async Task MoveServer(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            if(user == null) {
                await message.Channel.SendMessageAsync($"Cannot find user");
            } else if(user.GuildId == guild.Id) {
                await message.Channel.SendMessageAsync($"Already configured for the current server.");
            } else {
                if(user.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)socketUser).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }
                user.GuildId = guild.Id;
                await db.SaveChangesAsync();

                //var Response = await ContractsAPI.FirstContact(user.EggIncIds.First().Id);
                var Response = await apiLink.GetBackup(user.EggIncIds.First().Id);
                var earningsBonus = Response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == socketUser.Id);

                var role = await DiscordHelpers.SetRole(guild, guildUser, earningsBonus);

                var checkElite = await DiscordHelpers.CheckElite(guild, guildUser, new List<double> { earningsBonus });



                var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }


                var text = $"Welcome {socketUser.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                await guild.SendToGeneralChannel(text);
                await CleanWelcomeChannel(guild, socketUser);
            }
        }

        public static async Task RemoveEggName(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"Cannot find user");
            } else {
                user.RemoveName(args[0]);
            }

            await db.SaveChangesAsync();

            var json = JsonConvert.SerializeObject(user.EggIncIds, Formatting.Indented);

            await message.Channel.SendMessageAsync(await AccountsString(user, apiLink));
        }

        public static async Task RemoveID(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"Cannot find user");
            } else {
                user.RemoveID(args[0]);
            }

            await db.SaveChangesAsync();

            var json = JsonConvert.SerializeObject(user.EggIncIds, Formatting.Indented);

            await message.Channel.SendMessageAsync(await AccountsString(user, apiLink));
        }

        public static async Task LeaveCoop(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.First();

            var name = new Regex(@"\w+").Match(message.Channel.Name.ToLower()).Value;
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.Name.ToLower() == name);
            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == socketUser.Id);

            var xrefs = await db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == dbuser.Id && x.CoopId == coop.Id).ToListAsync();

            foreach(var xref in xrefs) {
                var res2 = await ContractsAPI.Send(new Ei.LeaveCoopRequest {
                    ClientVersion = 24,
                    ContractIdentifier = coop.ContractID,
                    CoopIdentifier = coop.Name,
                    PlayerIdentifier = xref.EggIncId,
                }, xref.EggIncId);
            }

            await message.Channel.SendMessageAsync($"Attempted to remove {socketUser.Mention} from co-op.");
        }

        //public static async Task AddEggName(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
        //    await message.Channel.SendMessageAsync($"Please use the command !register [eggincid]");

        //    return;
        //}

        public static async Task Accept(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            if(guild == null) {
                await message.Channel.SendMessageAsync("Unable to find server, please run this command in a server");
                return;
            }
            if(user == null) {
                user = new DBUser {
                    DiscordId = socketUser.Id,
                    DiscordUsername = socketUser.Username,
                    AcceptedRules = true,
                    CreateOn = DateTimeOffset.Now,
                    GuildId = guild.Id,
                    showEB = true
                };
                db.DBUsers.Add(user);
            } else {
                if(user.AcceptedRules) {
                    await message.Channel.SendMessageAsync($"ERROR: {socketUser.Mention} you have already accepted rules");
                    return;
                }
                user.AcceptedRules = true;
            }

            if(user.EggIncIds.Count > 0 && user.GuildId > 0) {
                await message.Channel.SendMessageAsync($"{socketUser.Mention}, looks like you are registered with another server, if you would like to move to this server use the command  __**!moveserver**__, if you want to stay on the old server but want to join in the conversation on this server message an admin.");
            } else {
                string channelText = "";
                var talkChannel = guild.TextChannels.FirstOrDefault(x => x.Id == 746509501271769210);
                if(talkChannel != null) {
                    channelText = $"If you have questions about this, feel free to message us in {talkChannel.Mention}";
                }
                string msg;
                if(user.EggIncIds.Count > 0) {
                    msg = $"{socketUser.Mention}, now run the command !moveserver";

                } else {
                    msg = $"{socketUser.Mention}, next we’ll need you to register with your Egg, Inc account. Please use the command `!register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track pre-farming and create balanced co-ops. The bot only reads certain parts of the info and does not make any changes. {channelText}";
                }

                await message.Channel.SendMessageAsync(msg);
                //await message.Channel.SendMessageAsync($"{socketUser.Mention} Now you need to register with your Egg, Inc account. Please use the command \"!register [Egg, Inc ID]\", look at the pinned message in this channel to see how to get your [Egg, Inc ID]. The bot needs everyone's ID to be able to track pre-farming and create balanced co-ops, the bot only reads certain parts of the info and does not make any changes.");
            }

            await db.SaveChangesAsync();
        }

        public static async Task UpdateID(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            if(args.Length == 0) {
                await message.Channel.SendMessageAsync($"Error: Missing ID, example !updateid EI#######");
                return;
            }
            //var Response = await ContractsAPI.FirstContact(args[0]);
            var Response = await apiLink.GetBackup(args[0]);


            if(Response == null || Response.Farms.Count == 0) {
                await message.Channel.SendMessageAsync($" {socketUser.Mention} Error:  Possibly wrong EggInc ID**");
                return;
            }
            if(Response.EggIncId != args[0]) {
                await message.Channel.SendMessageAsync($"Error matching ID {args[0]} - {Response.EggIncId}");
                return;
            }

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user.EggIncIds.Count > 1) {
                var argsClean = args.ToList().Where(x => !x.StartsWith("<"));
                if(argsClean.Count() < 2) {
                    var count = 1;
                    var accounts = String.Join("\n", user.Backups.Select(x => $"{count++} {x.UserName} Projected: {x.EarningsBonus.ToEggString()}"));
                    await message.Channel.SendMessageAsync($"User has multiple accounts, please specifiy which account `!updateid {{eggincid}} {{accountnumber}}`\n{accounts}");
                    return;
                }
                var account = int.Parse(argsClean.Last()) - 1;

                var eggIncIDs = new List<EggIncNameAndId>();
                for(var i = 0; i < user.EggIncIds.Count; i++) {
                    if(i == account)
                        eggIncIDs.Add(new EggIncNameAndId { Id = Response.EggIncId, Name = Response.UserName });
                    else
                        eggIncIDs.Add(user.EggIncIds[i]);
                }

                user.EggIncIds = eggIncIDs;
            } else {
                user.EggIncIds = new List<EggIncNameAndId> { new EggIncNameAndId { Id = Response.EggIncId, Name = Response.UserName } };
            }
            await db.SaveChangesAsync();

            await userstatus(message, args, db, _client, apiLink);

        }

        public static async Task Register(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            //var Response = await ContractsAPI.FirstContact(args[0]);
            var Response = await apiLink.GetBackup(args[0]);

            if(Response?.Farms == null || Response.Farms.Count == 0) {
                await message.Channel.SendMessageAsync($" {socketUser.Mention} Error:  Possibly wrong EggInc ID, make sure include any symbols and letters as described in: <https://discordapp.com/channels/656455567858073601/680431628950044676/735966079695978577>. **You can also send a screenshot and someone will help you register.**");
                return;
            }
            var addedUser = false;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                user = new DBUser {
                    DiscordId = socketUser.Id,
                    DiscordUsername = socketUser.Username,
                    EggIncIds = new List<EggIncNameAndId> { new EggIncNameAndId { Id = Response.EggIncId, Name = Response.UserName } },
                    CreateOn = DateTimeOffset.Now,
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == message.Channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(user);
                addedUser = true;
            } else {
                if(user.EggIncIds.Any(y => y.Id == Response.EggIncId)) {
                    await message.Channel.SendMessageAsync($"You are already registered with the bot. {socketUser.Mention}");
                    return;
                }
                if(user.EggIncIds.Count == 0) {
                    addedUser = true;
                }
                user.AddName(Response.UserName, Response.EggIncId);
            }

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));


            var existingBackups = user.Backups ?? new List<CustomBackup>();
            existingBackups.Add(Response);
            var earningsBonus = existingBackups.Max(x => x.EarningsBonus);



            IGuildUser socketGuildUser = null;
            try {
                socketGuildUser = (SocketGuildUser)socketUser;
            } catch(Exception) {
                try {
                    guild.Users.First(x => x.Id == socketUser.Id);
                } catch(Exception) {
                    socketGuildUser = await _client.Rest.GetGuildUserAsync(guild.Id, socketUser.Id);
                }
            }

            await db.SaveChangesAsync();

            var registeredRole = guild.Roles.First(x => x.Name.ToLower().Contains("registered"));
            //socketGuildUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(!socketGuildUser.RoleIds.Any(x => x == registeredRole.Id)) {
                await socketGuildUser.AddRoleAsync(registeredRole);
            }


            var role = await DiscordHelpers.SetRole(guild, socketGuildUser, earningsBonus);
            var checkElite = await DiscordHelpers.CheckElite(guild, socketGuildUser, existingBackups.Select(x => x.EarningsBonus).ToList());

            var roleText = "";
            if(existingBackups.Count > 1) {
                roleText = $"Your new account has been added with an EB of {Response.EarningsBonus.ToEggString()}";
            } else if(role != null) {
                roleText = $"You have been assigned the rank of {role?.Name} thanks to your EB of {earningsBonus.ToEggString()}.";
            }
            var faqText = "";
            var faqChannel = DiscordHelpers.GetFaqChannel(guild);
            if(faqChannel != null && existingBackups.Count == 1) {
                faqText = $"When you have a chance, read over {faqChannel.Mention} to get an idea on how the server and bot functions";
            }

            if(checkElite.Role != null) {
                roleText += $" You are eligible for {checkElite.Role.Name}s";
            }
            await guild.SendToGeneralChannel($"Welcome {socketUser.Mention}! {roleText}. {faqText}");



            await DiscordHelpers.CheckSiloResearch(guild, socketGuildUser, existingBackups);
            await DiscordHelpers.CheckHatchlingRole(guild, socketGuildUser, user);
            await DiscordHelpers.CheckFreshEggsRole(guild, socketGuildUser, user);

            var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
            if(activeRole != null) {
                await socketGuildUser.AddRoleAsync(activeRole);
            }



            var unjoinedRole = guild.Roles.FirstOrDefault(x => x.Id == 796512753241161748);
            if(unjoinedRole != null) {
                await socketGuildUser.AddRoleAsync(unjoinedRole);
            }

            var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
            if(overflowRole != null) {
                await socketGuildUser.AddRoleAsync(overflowRole);
            }

            try {
                var guildContracts = await db.GuildContracts.AsQueryable().Where(x => !x.DeletedChannel && x.GuildID == guild.Id).ToListAsync();
                foreach(var guildContract in guildContracts) {
                    var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");
                    skipList.Add(socketUser.Id);

                    guildContract.Skip = JsonConvert.SerializeObject(skipList);

                    var channel = guild.GetTextChannel(guildContract.DiscordChannelId);
                    if(channel != null) {
                        await channel.AddPermissionOverwriteAsync(socketUser, new OverwritePermissions(viewChannel: PermValue.Allow));
                    }
                }
            } catch(Exception) { }



            await CleanWelcomeChannel(guild, socketUser);
            if(addedUser) {
                try {
                    var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
                    var ebString = $" ({earningsBonus.ToEggString()})";
                    var newName = ebrgx.Replace(((IGuildUser)socketUser).GetName(), "").Trim().Truncate(32 - ebString.Length) + ebString;

                    await ((IGuildUser)socketUser).ModifyAsync(x => x.Nickname = newName);

                } catch(Exception) {

                }

            }
        }

        private static async Task CleanWelcomeChannel(SocketGuild guild, SocketUser socketUser, int chain = 0) {
            try {
                var welcomeChannel = guild.GetWelcomeChannel();
                if(welcomeChannel != null) {
                    var messages = await welcomeChannel.GetMessagesAsync().FlattenAsync();

                    var userMessage = messages.Where(x => x.MentionedUserIds.Contains(socketUser.Id) || x.Author.Id == socketUser.Id);

                    foreach(var message in userMessage.Where(x => x.CreatedAt <= DateTimeOffset.Now.AddDays(-13))) {
                        await message.DeleteAsync();
                    }
                    await welcomeChannel.DeleteMessagesAsync(userMessage.Where(x => x.CreatedAt > DateTimeOffset.Now.AddDays(-13)));
                }
            } catch(Exception) {
                if(chain < 3) {
                    await CleanWelcomeChannel(guild, socketUser, chain++);
                }
            }
        }

        public static async Task RemoveNull(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - User not registered");
                return;
            } else {
                var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(user._eggIncIds);
                eggIncIds.RemoveAll(x => x.Id == null);
                user.EggIncIds = eggIncIds; //Force JSON Update

            }

            await db.SaveChangesAsync();

            await message.Channel.SendMessageAsync(await AccountsString(user, apiLink));
        }

        public static async Task userstatus(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client, APILink apiLink) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Bot error - User not registered");
                return;
            }
            var msg = await AccountsString(user, apiLink);

            if(message.Channel is SocketDMChannel) {
                if(user.GuildId > 0) {
                    msg += $"\nRegistered with the server {_client.GetGuild(user.GuildId).Name}";
                } else {
                    msg += $"\nNot registered with a guild";
                }
            } else {
                if(user.GuildId == ((IGuildChannel)message.Channel).GuildId && !user.TempDisabled) {
                    msg += $"\nProperly registered with this server";
                }

                if(user.GuildId != ((IGuildChannel)message.Channel).GuildId) {
                    msg += $"\nNot registered with this server, try the !moveserver command";
                }
            }

            if(user.TempDisabled) {
                msg += $"\nUser is disabled, please !enable user";
            }


            await message.Channel.SendMessageAsync(msg);
        }


        private static async Task<string> AccountsString(DBUser user, APILink apiLink) {
            var msg = $"Egg Inc Account{(user.EggIncIds.Count > 1 ? "s" : "")}";
            foreach(var egginc in user.EggIncIds) {
                //var backup = await ContractsAPI.FirstContact(egginc.Id);//user.LastBackup.FirstOrDefault(x => x.GetID() == egginc.Id);
                var backup = await apiLink.GetBackup(egginc.Id);
                //if(egginc.Name != backup.UserName) {
                //user.UpdateNameAndId
                //}
                msg += $"\nName: {egginc.Name} Id: {egginc.Id}";
                if(backup?.Farms?.Count > 0) {
                    msg += $" EB: {backup.EarningsBonus.ToEggString()} Lastbackup: {(DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime)).Humanize()}";
                } else {
                    msg += " Backup Is Empty. Double check your ID.";
                }
            }
            return msg;
        }

        public static async Task RemoveDuplicates(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;

            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            var users = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();


            var userFilter = users.Where(x => x.EggIncIds.Any(y => y.Id == null) || x.EggIncIds.GroupBy(y => y.Name).Any(y => y.Count() > 1)).ToList();


            userFilter.ForEach(user => {
                var eggIncIds = JsonConvert.DeserializeObject<List<EggIncNameAndId>>(user._eggIncIds);


                var cleanedIds = eggIncIds.Where(x => x.Id != null).GroupBy(x => x.Id).Select(x => new EggIncNameAndId { Id = x.Key, Name = x.Last().Name }).ToList();

                user.EggIncIds = cleanedIds; //Force JSON Update

            });

            await db.SaveChangesAsync();


            await message.Channel.SendMessageAsync($"Done");
        }

        public static async Task ShowDuplicates(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            var users = await db.DBUsers.AsQueryable().AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();


            var userFilter = users.Where(x => x.EggIncIds.Any(y => y.Id == null) || x.EggIncIds.GroupBy(y => y.Name).Any(y => y.Count() > 1));

            await message.Channel.SendMessageAsync(String.Join("\n\n", userFilter.Select(user => user.DiscordUsername + "\n" + String.Join("\n", user.EggIncIds.Select(x => $"Name: {x.Name} Id: {x.Id}")))));
        }

        public static async Task TakeABreak(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Cannot find database user");
            }

            user.OnBreakSince = DateTimeOffset.Now;
            await db.SaveChangesAsync();

            await message.Channel.SendMessageAsync($"{socketUser.Mention} is set to take a break. This status will automatically be removed the next time you start pre-farming. If you are currently pre-farming you will still be assigned to a co-op unless you exit the contract.");
        }

        public static async Task Disable(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Cannot find database user");
            }

            user.TempDisabled = true;
            await db.SaveChangesAsync();

            await message.Channel.SendMessageAsync($"{socketUser.Mention} is disabled.");
        }

        public static async Task Enable(SocketMessage message, string[] args, ApplicationDbContext db, DiscordSocketClient _client) {
            SocketUser socketUser = message.MentionedUsers.Any() ? message.MentionedUsers.First() : message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Cannot find database user");
            }

            user.TempDisabled = false;
            await db.SaveChangesAsync();

            await message.Channel.SendMessageAsync($"{socketUser.Mention} is enabled and will be assigned to co-ops from now on.");
        }

        private static async Task _cleanWelcome(SocketMessage message, DiscordSocketClient _client) {
            await message.DeleteAsync();
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
            await guild.PruneUsersAsync(10);

            var welcomeChannel = guild.GetWelcomeChannel();

            var messages = await welcomeChannel.GetMessagesAsync(500).FlattenAsync();

            var toDeleteRecent = messages.Where(x => !x.IsPinned && x.CreatedAt < DateTime.Now.AddDays(-5) && x.CreatedAt > DateTime.Now.AddDays(-13)).ToList();
            var toDeleteOld = messages.Where(x => !x.IsPinned && x.CreatedAt <= DateTime.Now.AddDays(-13)).ToList();

            await ((SocketTextChannel)message.Channel).DeleteMessagesAsync(toDeleteRecent);

            foreach(var msg in toDeleteOld) {
                await Task.Delay(510);
                try {
                    await msg.DeleteAsync();
                } catch(HttpException) {
                    try {
                        await msg.DeleteAsync();
                    } catch(HttpException) {
                        Console.WriteLine("** Error deleting from welcome");
                    }

                }
            }
        }



        public static async Task Clean(SocketMessage message, DiscordSocketClient _client) {
            var channel = (SocketTextChannel)message.Channel;
            if(channel.Name.ToLower().Contains("welcome")) {
                await _cleanWelcome(message, _client);
            } else {
                await _cleanUnpinned(channel);
            }
        }

        private static async Task _cleanUnpinned(SocketTextChannel channel) {
            var messages = await channel.GetMessagesAsync(500).FlattenAsync();
            messages = messages.Where(x => !x.IsPinned);

            var recent = messages.Where(x => x.CreatedAt > DateTime.Now.AddDays(-13) && !x.IsPinned).ToList();
            var older = messages.Where(x => x.CreatedAt <= DateTime.Now.AddDays(-13) && !x.IsPinned).ToList();

            await channel.DeleteMessagesAsync(recent);

            foreach(var msg in older) {
                await Task.Delay(510);
                await msg.DeleteAsync();
            }
        }

        public static async Task Say(SocketMessage message, string[] args) {
            var channel = (SocketTextChannel)message.MentionedChannels.First();
            await channel.SendMessageAsync(String.Join(" ", args.Skip(1)));
        }

        public static async Task ShowEB(SocketMessage message, string[] args, ApplicationDbContext db) {
            IGuildUser discordUser = message.MentionedUsers.Any() ? (SocketGuildUser)message.MentionedUsers.First() : (SocketGuildUser)message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == discordUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Cannot find database user");
                return;
            }
            if(user.showEB) {
                await message.Channel.SendMessageAsync($"The bot is already set to update your EB automatically. It will update every {LeaderboardUpdater.UpdateTime.TotalMinutes} mins when the leaderboard does.");
                return;
            }

            var higherEB = user.Backups.OrderByDescending(x => x.EarningsBonus).First();

            var eb = higherEB.EarningsBonus.ToEggString();
            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            var ebString = $" ({eb})";
            var newName = ebrgx.Replace(((IGuildUser)discordUser).GetName(), "").Trim().Truncate(32 - ebString.Length) + ebString;

            await discordUser.ModifyAsync(x => x.Nickname = newName);

            user.showEB = true;
            await db.SaveChangesAsync();




            await message.Channel.SendMessageAsync($"{discordUser.Mention} will be updated with their EB. To stop this run the command !hideEB");
        }

        public static async Task HideEB(SocketMessage message, string[] args, ApplicationDbContext db) {
            IGuildUser socketUser = message.MentionedUsers.Any() ? (SocketGuildUser)message.MentionedUsers.First() : (SocketGuildUser)message.Author;
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == socketUser.Id);
            if(user == null) {
                await message.Channel.SendMessageAsync($"ERROR: Cannot find database user");
            }

            user.showEB = false;
            await db.SaveChangesAsync();



            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            var newName = ebrgx.Replace(((IGuildUser)socketUser).GetName(), "");

            await socketUser.ModifyAsync(x => x.Nickname = newName);


            await message.Channel.SendMessageAsync($"{socketUser.Mention} will no longer be updated with their EB.");
        }

    }
}

