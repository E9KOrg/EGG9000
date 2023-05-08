using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;


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
using EGG9000.Common.Helpers;
using EGG9000.Common.Commands;
using static EGG9000.Common.Database.Entities.DBUser;
using Ei;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Commands {
    public static class RegisterCommandsSlash {


        [SlashCommand(Description = "Use to move registration to a different discord server")]
        public static async Task MoveServer(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(user == null) {
                await command.RespondAsync($"Cannot find user");
            } else if(user.GuildId == guild.Id) {
                await command.RespondAsync($"Already configured for the current server.");
            } else {
                await command.RespondAsync($"Please wait...");
                if(user.GuildId == 428181243474214942) {
                    await ((SocketGuildUser)command.User).AddRoleAsync(guild.Roles.FirstOrDefault(x => x.Name == "Prophet"));
                }
                user.GuildId = guild.Id;
                await db.SaveChangesAsync();

                //var Response = await ContractsAPI.FirstContact(user.EggIncIds.First().Id);
                var Response = await apiLink.GetBackup(user.EggIncAccounts.First().Id);
                var earningsBonus = Response.EarningsBonus;

                var guildUser = guild.Users.First(x => x.Id == command.User.Id);

                var role = await DiscordHelpers.SetRole(guild, guildUser, earningsBonus);

                _ = await DiscordHelpers.CheckLeague(guild, guildUser, new List<double> { earningsBonus });



                var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == guild.Id);
                if(dbguild != null && dbguild.OverflowServers.Count > 0) {
                    var overflowRole = guild.Roles.FirstOrDefault(x => x.Id == 775547850134257675);
                    if(overflowRole != null) {
                        await guildUser.AddRoleAsync(overflowRole);
                    }
                }

                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel.Id == command.Channel.Id) {
                    await command.DeleteOriginalResponseAsync();
                    var text = $"Welcome {command.User.Mention}, you have been moved to this server. You have the rank of {role?.Name} with an EB of {earningsBonus.ToEggString()}";
                    var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
                    await generalChannel.SendMessageAsync(text);
                    await CleanWelcomeChannel(guild, _client, command.User);
                } else {
                    await command.ModifyOriginalResponseAsync("Registration has been moved");
                }
            }
        }

        [SlashCommand(Description = "Removed registered EggInc ID from your account", AdminOnly = true, ParentCommand = "a")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid, [SlashParam] SocketGuildUser targetUser) {
            return _RemoveID(command, db, apiLink, eggincid, targetUser.Id);
        }
        /* Moving to a staff-only command, leaving commented out in case of re-activation in future
         * [SlashCommand(Description = "Removed registered EggInc ID from your account")]
        public static Task RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, [SlashParam] string eggincid) {
            return _RemoveID(command, db, apiLink, eggincid, command.User.Id);
        }*/
        public static async Task _RemoveID(FauxCommand command, ApplicationDbContext db, APILink apiLink, string eggincid, ulong userid) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == userid);
            if(user == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find user");
                return;
            } else if(user.EggIncAccounts.Any(x => x.Id == eggincid)) {
                user.RemoveID(eggincid);
                user.Backups = user.Backups.Where(x => x.EggIncId != eggincid).ToList();
            } else {
                await command.RespondAsync($"⚠️ERROR: Unable to find the following EggIncId {eggincid} \n {await AccountsString(db, user, apiLink, false)}");
                return;
            }

            await db.SaveChangesAsync();

            var json = JsonConvert.SerializeObject(user.EggIncAccounts, Formatting.Indented);

            await command.RespondAsync($"ID removed\n{await AccountsString(db, user, apiLink, false)}");
        }

        [SlashCommand(Description = "Used to remove a user from a co-op to fix a glitch.", AdminOnly = true)]
        public static async Task LeaveCoop(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Command can only be used in a co-op channel");
                return;
            }
            var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == targetUser.Id);

            var xrefs = await db.UserCoopXrefs.AsQueryable().Where(x => x.UserId == dbuser.Id && x.CoopId == coop.Id).ToListAsync();

            foreach(var xref in xrefs) {
                var res2 = await ContractsAPI.Send(new Ei.LeaveCoopRequest {
                    ClientVersion = 24,
                    ContractIdentifier = coop.ContractID,
                    CoopIdentifier = coop.Name,
                    PlayerIdentifier = xref.EggIncId,
                }, xref.EggIncId);
            }

            await command.RespondAsync($"Attempted to remove {targetUser.Mention} from co-op.");
        }


        [SlashCommand(Description = "Accept the rules of this discord server")]
        public static async Task Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client) {
            await _Accept(command, db, _client, command.User);
        }
        [SlashCommand(Description = "Accept the rules of this discord server", AdminOnly = true, AllowFarmHand = true, ParentCommand = "a")]
        public static async Task Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser) {
            await _Accept(command, db, _client, targetUser);
        }
        public static async Task _Accept(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IUser targetUser) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            if(guild == null) {
                await command.RespondAsync("Unable to find server, please run this command in a server");
                return;
            }
            if(user == null) {
                user = new DBUser {
                    DiscordId = targetUser.Id,
                    DiscordUsername = targetUser.Username,
                    AcceptedRules = true,
                    CreateOn = DateTimeOffset.Now,
                    GuildId = guild.Id,
                    showEB = true
                };
                db.DBUsers.Add(user);
            } else {
                if(user.AcceptedRules) {
                    await command.RespondAsync($"⚠️ERROR: {targetUser.Mention} you have already accepted rules");
                    return;
                }
                user.AcceptedRules = true;
            }

            if(user.EggIncAccounts.Count > 0 && user.GuildId > 0) {
                await command.RespondAsync($"{targetUser.Mention}, looks like you are registered with another server, if you would like to move to this server use the </moveserver:1095116354329268366> command");
            } else {
                string channelText = "";
                var talkChannel = guild.TextChannels.FirstOrDefault(x => x.Id == 746509501271769210);
                if(talkChannel != null) {
                    channelText = $"If you have questions about this, feel free to message us in {talkChannel.Mention}";
                }
                if(user.EggIncAccounts.Count > 0) {
                    if(user.GuildId != guild.Id) {
                        await command.RespondAsync($"{targetUser.Mention}, now run the </moveserver:1095116354329268366> command");
                    } else if(user.TempDisabled) {
                        await command.RespondAsync($"Looks like you are currently disabled, please wait for someone from staff to get you re-enabled.");
                    } else {
                        var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
                        await generalChannel.SendMessageAsync($"Welcome back {targetUser.Mention}!");


                        var activeRole = guild.Roles.FirstOrDefault(x => x.Id == 798284088967430144);
                        if(activeRole != null) {
                            await ((SocketGuildUser)targetUser).AddRoleAsync(activeRole);
                        }

                        await CleanWelcomeChannel(guild, _client, targetUser);
                    }

                } else {
                    await command.RespondAsync($"{targetUser.Mention}, next we’ll need you to register with your Egg, Inc account. Please use the command `/register EI#####`, where EI##### is your Egg Inc ID, to find your ID please go to Settings, then Privacy & Data, and find the letters & numbers in the bottom center of the window. More detailed instructions are included in the pinned messages of this channel.\n\nWhy do we need this? The bot needs everyone's ID to be able to track pre-farming and create balanced co-ops. The bot only reads certain parts of the info and does not make any changes. {channelText}");
                }

            }

            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Update your EggIncID if it has changed")]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, _client, apiLink, eggincid, (SocketGuildUser)command.User, accountnumber);
        }
        [SlashCommand(Description = "EggIncID someones ID", AdminOnly = true, ParentCommand = "a")]
        public static async Task UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID starting with EI")] string eggincid,[SlashParam]SocketGuildUser targetUser, [SlashParam(Description = "Account Number (if you have more than one)", Required = false)] int accountnumber = 0) {
            await _UpdateID(command, db, _client, apiLink, eggincid, targetUser, accountnumber);
        }
        public static async Task _UpdateID(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, string eggincid, SocketGuildUser targetUser, int accountnumber) {
            var Response = await apiLink.GetBackup(eggincid);


            if(Response == null || Response.Farms == null || Response.Farms.Count == 0) {
                await command.RespondAsync($" {command.User.Mention} Error:  Possibly wrong EggInc ID**", ephemeral: true);
                return;
            }
            if(Response.EggIncId != eggincid) {
                await command.RespondAsync($"Error matching ID {eggincid} - {Response.EggIncId}", ephemeral: true);
                return;
            }

            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == targetUser.Id);
            if(user.EggIncAccounts.Count > 1) {
                if(accountnumber == 0) {
                    var count = 1;
                    var accounts = String.Join("\n", user.Backups.Select(x => $"{count++} {x.UserName} EB: {x.EarningsBonus.ToEggString()}"));
                    await command.RespondAsync($"User has multiple accounts, please specifiy which account `/updateid {{eggincid}} {{accountnumber}}`\n{accounts}", ephemeral: true);
                    return;
                }
                var account = accountnumber - 1;

                var eggIncIDs = new List<EggIncAccount>();
                for(var i = 0; i < user.EggIncAccounts.Count; i++) {
                    if(i == account)
                        eggIncIDs.Add(new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName });
                    else
                        eggIncIDs.Add(user.EggIncAccounts[i]);
                }

                user.EggIncAccounts = eggIncIDs;
            } else {
                user.EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName } };
            }
            await db.SaveChangesAsync();

            await command.RespondAsync($"ID Update\n{await AccountsString(db, user, apiLink, false)}", ephemeral: true);

        }

        [SlashCommand(Description = "Register your EggInc account with the bot", AdminOnly = true, ParentCommand = "a")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID which begins with EI followed by numbers")] string eggincid, [SlashParam] SocketGuildUser user) {
            return _Register(command, db, _client, apiLink, eggincid, user);
        }
        [SlashCommand(Description = "Register your EggInc account with the bot")]
        public static Task Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID which begins with EI followed by numbers")] string eggincid) {
            return _Register(command, db, _client, apiLink, eggincid, command.User);
        }
        public static async Task _Register(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam(Description = "EggIncID which begins with EI followed by numbers")] string eggincid, IUser user) {
            await command.RespondAsync("Processing...");
            eggincid = eggincid.ToUpper();
            var Response = await apiLink.GetBackup(eggincid);
            if(Response?.Farms == null || Response.Farms.Count == 0) {
                var id = new Regex(@"\d+").Match(eggincid).Value;
                if(eggincid.StartsWith("E1")) {
                    id = id.Substring(1);
                }
                if(id.Length > 7) {
                    Response = await apiLink.GetBackup(eggincid);
                }
            }

            if(Response?.Farms == null || Response.Farms.Count == 0) {
                await command.ModifyOriginalResponseAsync(m => m.Content = $" {user.Mention} Error:  Possibly wrong EggInc ID ({eggincid}), it should start with the capital letters EI followed by numbers. **You can also send a screenshot and someone will help you register.**");
                return;
            }
            var addedUser = false;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                dbuser = new DBUser {
                    DiscordId = user.Id,
                    DiscordUsername = user.Username,
                    EggIncAccounts = new List<EggIncAccount> { new EggIncAccount { Id = Response.EggIncId, Name = Response.UserName } },
                    CreateOn = DateTimeOffset.Now,
                    GuildId = _client.Guilds.First(x => x.TextChannels.Any(y => y.Id == command.Channel.Id)).Id,
                    showEB = true
                };
                db.DBUsers.Add(dbuser);
                addedUser = true;
            } else {
                if(dbuser.EggIncAccounts.Any(y => y.Id == Response.EggIncId)) {
                    await command.ModifyOriginalResponseAsync(m => m.Content = $"You are already registered with the bot. {user.Mention}");
                    return;
                }
                if(dbuser.EggIncAccounts.Count == 0) {
                    addedUser = true;
                }
                dbuser.AddName(Response.UserName, Response.EggIncId);
            }
            if(!dbuser.Registered.HasValue) {
                dbuser.Registered = DateTimeOffset.Now;
            }
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));


            var existingBackups = dbuser.Backups ?? new List<CustomBackup>();
            existingBackups.Add(Response);
            var earningsBonus = existingBackups.Max(x => x.EarningsBonus);



            IGuildUser socketGuildUser = null;
            try {
                socketGuildUser = (SocketGuildUser)user;
            } catch(Exception) {
                try {
                    guild.Users.First(x => x.Id == user.Id);
                } catch(Exception) {
                    socketGuildUser = await _client.Rest.GetGuildUserAsync(guild.Id, user.Id);
                }
            }

            await db.SaveChangesAsync();

            var registeredRole = guild.Roles.First(x => x.Name.ToLower().Contains("registered"));
            //socketGuildUser.Roles.FirstOrDefault(x => x.Name.ToLower().Contains("registered"));
            if(!socketGuildUser.RoleIds.Any(x => x == registeredRole.Id)) {
                await socketGuildUser.AddRoleAsync(registeredRole);
            }


            var role = await DiscordHelpers.SetRole(guild, socketGuildUser, earningsBonus);
            var checkLeague = await DiscordHelpers.CheckLeague(guild, socketGuildUser, existingBackups.Select(x => x.EarningsBonus).ToList());

            var roleText = "";
            if(existingBackups.Count > 1) {
                roleText = $"Your new account has been added with an EB of {Response.EarningsBonus.ToEggString()}";
            } else if(role != null) {
                roleText = $"You have been assigned the rank of {role?.Name} thanks to your EB of {earningsBonus.ToEggString()}.";
            }
            var faqText = "";
            var faqChannel = await _client.GetChannelAsync(GuildChannelType.FaqChannel, guild);
            if(faqChannel != null && existingBackups.Count == 1) {
                faqText = $"When you have a chance, read over {faqChannel.Mention} to get an idea on how the server and bot functions";
            }

            if(checkLeague.Role != null) {
                roleText += $" Your Grade is {checkLeague.Role.Name}";
            }
            var generalChannel = await _client.GetChannelAsync(GuildChannelType.General, guild);
            await generalChannel.SendMessageAsync($"Welcome {user.Mention}! {roleText}. {faqText}");



            await DiscordHelpers.CheckSiloResearch(guild, socketGuildUser, existingBackups);
            await DiscordHelpers.CheckHatchlingRole(guild, socketGuildUser, dbuser);
            await DiscordHelpers.CheckFreshEggsRole(guild, socketGuildUser, dbuser);

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
                    var channel = guild.GetTextChannel(guildContract.DiscordChannelId);
                    if(channel != null) {
                        await channel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                    }
                }
            } catch(Exception) { }



            await CleanWelcomeChannel(guild, _client, user);
            if(addedUser) {
                try {
                    var ebString = $" ({earningsBonus.ToEggString()})";
                    var newName = ((IGuildUser)user).GetCleanName().Trim().Truncate(32 - ebString.Length) + ebString;

                    await ((IGuildUser)user).ModifyAsync(x => x.Nickname = newName);

                } catch(Exception) {

                }

            }
            await command.DeleteResponseFix();
        }

        public static async Task CleanWelcomeChannel(SocketGuild guild, DiscordHostedService _client, IUser socketUser, int chain = 0) {
            try {
                var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);
                if(welcomeChannel != null) {
                    var messages = await welcomeChannel.GetMessagesAsync().FlattenAsync();

                    var userMessage = messages.Where(x => x.MentionedUserIds.Contains(socketUser.Id) || x.Author.Id == socketUser.Id);

                    await welcomeChannel.DeleteMessagesBatchAsync(userMessage);
                }
            } catch(Exception) {
                if(chain < 3) {
                    await CleanWelcomeChannel(guild, _client, socketUser, chain++);
                }
            }
        }


        [SlashCommand(Description = "Get a users status", AdminOnly = true, ParentCommand = "a")]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, [SlashParam] SocketGuildUser user, [SlashParam(Required = false)] bool ShowInChannel = false) {
            return _userstatus(command, db, _client, apiLink, user, true, ShowInChannel);
        }

        [SlashCommand(Description = "Get your status")]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink) {
            return _userstatus(command, db, _client, apiLink, command.User, false, false);
        }
        public static async Task _userstatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, APILink apiLink, IUser user, bool admin = false, bool showInChannel = false) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - User not registered", ephemeral: !showInChannel);
                return;
            }
            var msg = await AccountsString(db, dbuser, apiLink, admin);

            if(command.Channel is SocketDMChannel) {
                if(dbuser.GuildId > 0) {
                    msg += $"\nRegistered with the server {_client.GetGuild(dbuser.GuildId).Name}";
                } else {
                    msg += $"\nNot registered with a guild";
                }
            } else {
                var channelGuildId = ((IGuildChannel)command.Channel).GuildId;
                var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == channelGuildId || x.OverflowServersJson.Contains(channelGuildId.ToString()));
                if(dbuser.GuildId == guild.Id && !dbuser.TempDisabled) {
                    msg += $"\nProperly registered with this server";
                }

                if(dbuser.GuildId != guild.Id) {
                    msg += $"\nNot registered with this server, try the </moveserver:1095116354329268366> command";
                }
            }

            if(dbuser.TempDisabled) {
                msg += $"\nUser is disabled";
            }

            msg += $"\nJoined the bot on {dbuser.Registered.Value.ToString("MMM dd, yyyy")}";

            await command.RespondAsync(msg, ephemeral: !showInChannel);
        }


        private static async Task<string> AccountsString(ApplicationDbContext db, DBUser user, APILink apiLink, bool admin) {
            var msg = $"Egg Inc Account{(user.EggIncAccounts.Count > 1 ? "s" : "")}";
            foreach(var egginc in user.EggIncAccounts) {
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

                //Determine user's grade emoji based on LastGrade
                var gradeEmoji = egginc.LastGrade switch {
                    Ei.Contract.Types.PlayerGrade.GradeAaa => "<:grade_aaa:1102985471862247426>",
                    Ei.Contract.Types.PlayerGrade.GradeAa => "<:grade_aa:1102985434562297989>",
                    Ei.Contract.Types.PlayerGrade.GradeA => "<:grade_a:1102985380845867068>",
                    Ei.Contract.Types.PlayerGrade.GradeB => "<:grade_b:1102985335920668742>",
                    Ei.Contract.Types.PlayerGrade.GradeC => "<:grade_c:1102984452935794799>",
                    _ => "None",
                };
                msg += "\nGrade: " + gradeEmoji;
                msg += "\nBoarding Group: " + (egginc.Group == 0 ? "**None**" : "BG" + egginc?.Group);
                //Create a filter string
                var filterString = "";
                foreach(var r in egginc.AutoRegisterRewards){
                    filterString += (r.ToString() + ", ");
                }
                //Remove trailing ", "
                filterString = filterString.Remove(filterString.Length - 2);
                msg += "\nFilter: " + filterString ?? "No Filter";
                msg += "\nBreak: " + (egginc.OnBreakUntil == default ? "Not on break" : "On break until <t:" + egginc.OnBreakUntil.ToUnixTimeSeconds() + ":f>");
            }
            if(admin) {
                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.UserId == user.Id && !x.Coop.DeletedChannel).ToListAsync();
                msg += $"\n{string.Join("\n", xrefs.Select(x => $"<#{x.Coop.DiscordChannelId}> {(user.EggIncAccounts.Count > 1 ? $"({user.EggIncAccounts.FirstOrDefault(y => y.Id == x.EggIncId)?.Name})" : "")}"))}";
                msg += $"\nRecent Demerit:\n{await DemeritCommands.GetDemerits(user.Id, db)}";
            }
            return msg;
        }

        [Obsolete("TakeABreak is deprecated, please use MyContractSettings instead.")]
        [SlashCommand(Description = "Please use the `mycontractsettings` command instead")]
        public static async Task TakeABreak(FauxCommand command) {
            await command.RespondAsync("Please use the </mycontractsettings:1100476258518839336> command to take a break.", ephemeral: true);
        }

        [SlashCommand(Description = "Disable user, user will not be assigned to co-ops until re-enabled", AdminOnly = true)]
        public static async Task Disable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            dbuser.TempDisabled = true;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is disabled.");
        }

        [SlashCommand(Description = "Re-enable user", AdminOnly = true)]
        public static async Task Enable(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketGuildUser user) {
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            dbuser.TempDisabled = false;
            await db.SaveChangesAsync();

            await command.RespondAsync($"{user.Mention} is enabled and will be assigned to co-ops from now on.");
        }

        private static async Task _cleanWelcome(FauxCommand command, DiscordHostedService _client) {
            var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            await guild.PruneUsersAsync(10);

            var welcomeChannel = await _client.GetChannelAsync(GuildChannelType.Welcome, guild);

            var messages = await welcomeChannel.GetMessagesAsync(500).FlattenAsync();

            await (welcomeChannel).DeleteMessagesBatchAsync(messages);

            await command.DeleteResponseFix();
        }



        [SlashCommand(Description = "Removes any unpinned messages from the channel", AdminOnly = true, ParentCommand = "a")]
        public static async Task Clean(FauxCommand command, DiscordHostedService _client) {
            await command.RespondAsync("Cleaning...");
            var channel = (SocketTextChannel)command.Channel;
            if(channel.Name.ToLower().Contains("welcome")) {
                await _cleanWelcome(command, _client);
            } else {
                await _cleanUnpinned(command);
            }
        }

        private static async Task _cleanUnpinned(FauxCommand command) {
            var messages = await command.Channel.GetMessagesAsync(500).FlattenAsync();
            messages = messages.Where(x => !x.IsPinned);

            await ((SocketTextChannel)command.Channel).DeleteMessagesBatchAsync(messages);

            await command.DeleteResponseFix();
        }


        [SlashCommand(Description = "Have to bot keep add your EB to your nickname in this server (will auto update)")]
        public static async Task ShowEB(FauxCommand command, ApplicationDbContext db) {
            var dbUser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
                return;
            }
            if(dbUser.showEB) {
                await command.RespondAsync($"The bot is already set to update your EB automatically. It will update every {LeaderboardUpdater.UpdateTime.TotalMinutes} mins when the leaderboard does.", ephemeral: true);
                return;
            }

            //var higherEB = user.Backups.OrderByDescending(x => x.EarningsBonus).First();

            var ebs = dbUser.Backups.Where(x => dbUser.EggIncAccounts.Any(y => y.Id == x.EggIncId)).OrderByDescending(x => x.EarningsBonus).Select(x => x.EarningsBonus.ToEggString());
            var ebString = $" ({string.Join(",", values: ebs)})";
            var newName = ((IGuildUser)command.User).GetCleanName().Truncate(32 - ebString.Length) + ebString;

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);

            dbUser.showEB = true;
            await db.SaveChangesAsync();


            await command.RespondAsync($"{command.User.Mention} will be updated with their EB. To stop this run the command /hideEB", ephemeral: true);
        }

        [SlashCommand(Description = "Remove the EB from your nickname")]
        public static async Task HideEB(FauxCommand command, ApplicationDbContext db) {
            var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync($"⚠️ERROR: Cannot find database user");
            }

            user.showEB = false;
            await db.SaveChangesAsync();



            var ebrgx = new Regex(@"\(\d+.?\d*\w?\)");
            var newName = ((IGuildUser)command.User).GetCleanName();

            await ((SocketGuildUser)command.User).ModifyAsync(x => x.Nickname = newName);


            await command.RespondAsync($"{command.User.Mention} will no longer be updated with their EB.", ephemeral: true);
        }

        [SlashCommand(Description = "Kick and user and send them a link to an appeal form", AdminOnly = true)]
        public static async Task Kick(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketGuildUser targetUser, [SlashParam] string reason) {
            try {
                var dmChannel = await targetUser.CreateDMChannelAsync();
                var guild = _client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
                await dmChannel.SendMessageAsync($"You have been kicked from {guild.Name} for the reason: {reason}\n\nHere is an appeal form if you would like the rejoin the server: https://forms.gle/NqrqnDZzJ7YaqpAfA");
                await targetUser.KickAsync();
                await command.RespondAsync("Kicked with DM");
            } catch(HttpException) {
                await command.RespondAsync("Unable to send DM, user is not yet kicked");
            }
        }
    }
}

