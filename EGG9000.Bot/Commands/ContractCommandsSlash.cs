using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Services;
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
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Migrations;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;
using Bugsnag.Payload;
using System.Security.Principal;
using static Ei.Contract.Types;
using Ei;

namespace EGG9000.Bot.Commands {
    public static class ContractCommandsSlash {
        [SlashCommand(Description = "Makes a co-op public", AdminOnly = true)]
        public static async Task MakePublic(FauxCommand command, ApplicationDbContext db) {
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = true,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now public.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be public.");
                //await command.RespondAsync($"⚠️ERROR: {response.Message}");
            }
        }

        [SlashCommand(Description = "Move a user to a different grade of coop", AdminOnly = true, AllowFarmHand = true)]
        public static async Task MoveGrade(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountChannelSpecificAutoComplete))] string useraccount,
            [SlashParam(AutocompleteHandler = typeof(MoveGradeAutoComplete))] uint newgrade) {
            await command.RespondAsync("Please wait...");
            var targetCoop = await db.Coops.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Please use in a co-op channel");
                return;
            }

            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.FirstOrDefault(x => x.Id == useraccount.Split("|")[1]);

            /* Find a new co-op */
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.GuildId == targetCoop.GuildId && x.ContractID == targetCoop.ContractID && x.League == newgrade 
                && x.CurrentUsers < x.MaxUsers && (int)x.Status > 2 && (int)x.Status < 13 && x.CoopEnds > DateTimeOffset.Now).ToListAsync();

            if(coops.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}");
                return;
            }

            Coop newCoop = null;
            var contract = await db.Contracts.FirstOrDefaultAsync(x => x.ID == targetCoop.ContractID);
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, newgrade, usersWithBackups, _client, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: No open spots found for {PlayerGradeDetails.GetEmoji((PlayerGrade)newgrade)} {targetCoop.Contract.Name}");
                return;
            }
            /* END Find a new co-op */

            /* MOVING TO NEW COOP */
            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Name, discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }
            db.Add(newxref);
            /* END MOVING TO NEW COOP */

            /* REMOVING FROM OLD COOP */
            var userid2 = Guid.Parse(useraccount.Split("|")[0]);
            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == userid2 && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();
            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unabled to find user in co-op");
                return;
            }

            db.Remove(xref);
            /* END REMOVING FROM OLD COOP */

            await command.RespondAsync($"Removed {discordUser.Mention} ({account.Name}) from {((ITextChannel)command.Channel).Mention}, and moved to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }

        public enum FindCoopPrioritization {
            FinishTimeLow = 0,
            FinishTimeHigh = 1,
            LowPlayerCount = 2
        }

        [SlashCommand(Description="Attempt to find a coop for a user, move user to said coop", AdminOnly = true, AllowFarmHand = true)]
        public static async Task FindCoopForUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, 
            [SlashParam(AutocompleteHandler = typeof(ContractAutoComplete))] string contractid, [SlashParam(Required = false)]FindCoopPrioritization priority = FindCoopPrioritization.FinishTimeLow) {
            await command.DeferAsync();
            var guildRef = await db.Guilds.FirstOrDefaultAsync(g => g.Id == command.GuildId || g.OverflowServersJson.Contains(command.GuildId.ToString())); 
            var contract = await db.Contracts.FirstOrDefaultAsync(c => c.ID == contractid);
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.FirstOrDefault(x => x.Id == useraccount.Split("|")[1]);
            var userXrefs = await db.UserCoopXrefs.Include(x => x.Coop).ThenInclude(x => x.Contract).Include(x => x.Coop).Where(x => x.EggIncId == account.Id).ToListAsync();

            var existingCoop = userXrefs.FirstOrDefault(r => r.Coop.Contract == contract && (int)r.Coop.Status > 2 && (int)r.Coop.Status < 13 && r.Coop.CoopEnds > DateTimeOffset.Now);

            if(contract.cc_only && account.SubscriptionLevel == 0) {
                await command.RespondAsync($"⚠️ERROR: Non-subscribed account cannot be assigned to subscriber-only contract");
                return;
            } else if(existingCoop is not null) {
                await command.RespondAsync($"⚠️ERROR: User is already assigned a coop for contract {contract.Name}: <#{existingCoop.Coop.DiscordChannelId}>");
                return;
            } else if(account.GetGrade() is PlayerGrade.GradeUnset) {
                await command.RespondAsync($"⚠️ERROR: User does not have a grade set, and cannot be moved into a coop");
                return;
            }

            var coops = await db.Coops.Include(c => c.Contract).Include(c => c.UserCoopsXrefs).Where(c => c.Contract == contract && c.GuildId == guildRef.Id && c.League == (uint)account.GetGrade()
             && c.CurrentUsers < c.MaxUsers && (int)c.Status > 2 && (int)c.Status < 13 && c.CoopEnds > DateTimeOffset.Now).ToListAsync();

            if(!coops.Any()) {
                await command.RespondAsync($"⚠️ERROR: No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name}");
                return;
            }

            _ = priority switch {
                FindCoopPrioritization.FinishTimeLow => coops = coops.OrderBy(c => c.ProjectedFinish).ToList(),
                FindCoopPrioritization.FinishTimeHigh => coops = coops.OrderByDescending(c => c.ProjectedFinish).ToList(),
                FindCoopPrioritization.LowPlayerCount => coops = coops.OrderBy(c => c.UserCoopsXrefs.Count).ToList(),
                _ => coops = coops.OrderBy(c => c.CoopEnds).ToList(),
            };

            Coop newCoop = null;
            foreach(var coop in coops) {
                var userids = coop.UserCoopsXrefs.Select(x => x.UserId).ToList();
                var users = await db.DBUsers.Where(x => userids.Contains(x.Id)).ToListAsync();
                var usersWithBackups = users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Account = y, Backup = y.Backup, User = x })).ToList();
                var details = new CoopDetails(coop, contract, (uint)account.GetGrade(), usersWithBackups, _client, coop.LastStatusUpdate);
                if(details.HasSpots) {
                    newCoop = coop;
                    break;
                }
            }

            if(newCoop is null) {
                await command.RespondAsync($"⚠️ERROR: No open Grade {PlayerGradeDetails.GetEmoji(account.GetGrade())} coop spots found for {contract.Name}");
                return;
            }

            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(newCoop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(newCoop, dbuser.Id, account.Id, account.Name, discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);
            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(newCoop.GuildId != newCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }
            db.Add(newxref);

            await command.RespondAsync($"Sucessfully moved {discordUser.Mention} ({account.Name}) to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }

        [SlashCommand(Description = "Makes this co-op private", AdminOnly = true)]
        public static async Task MakePrivate(FauxCommand command, ApplicationDbContext db) {
            var name = new Regex(@"\w+").Match(command.Channel.Name.ToLower()).Value;
            var coop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(coop == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find coop for this channel {command.Channel.Name}");
                return;
            }

            var response = await ContractsAPI.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Public = false,
                RequestingUserId = coop.CreatorID
            }, coop.CreatorID);

            if(response.Success) {
                await command.RespondAsync($"{coop.Name} is now private.");
            } else {
                await command.RespondAsync($"{coop.Name} should now be private.");
            }
        }

        //[SlashCommand(Description = "Adds prefarmers from selected contract to this channel", AdminOnly = true)]
        //public static async Task AddPrefarmers(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
        //        return;
        //    }
        //    await command.RespondAsync($"Please wait...adding prefarmers");

        //    var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

        //    var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //    foreach(var user in coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants)) {
        //        await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
        //    }

        //    foreach(var user in coopsBreakdown.ExpiredFarms) {
        //        await ((ITextChannel)command.Channel).AddPermissionOverwriteAsync(user.DiscordUser, new OverwritePermissions(viewChannel: PermValue.Allow));
        //    }

        //    await command.Channel.SendMessageAsync($"{coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Count()} prefarmers added");
        //}


        [SlashCommand(Description = "Adds an outside co-op so you can track it's progress", AdminOnly = true, AllowFarmHand = true)]
        public static async Task AddCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname, [SlashParam] string grade) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
                return;
            }

            int league = 0;
            switch(grade.ToLower().Trim()) {
                case "aaa":
                    league = 5;
                    break;
                case "aa":
                    league = 4;
                    break;
                case "a":
                    league = 3;
                    break;
                case "b":
                    league = 2;
                    break;
                case "c":
                    league = 1;
                    break;
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
                    League = (uint)league,
                    CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
                };
                db.Coops.Add(coop);
                await db.SaveChangesAsync();
                await command.RespondAsync($"Co-op Added: {coopname} for {((SocketTextChannel)contractchannel).Mention}");
                return;
            } else {
                await command.RespondAsync($"⚠️ERROR: Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention}).");
                return;
            }
        }


        //[SlashCommand(Description = "Start a new co-op")]
        //public static async Task NewCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] SocketChannel contractchannel, [SlashParam] string coopname, [SlashParam] string grade) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
        //        return;
        //    }

        //    int league = 0;
        //    switch(grade.ToLower().Trim()) {
        //        case "aaa":
        //            league = 5;
        //            break;
        //        case "aa":
        //            league = 4;
        //            break;
        //        case "a":
        //            league = 3;
        //            break;
        //        case "b":
        //            league = 2;
        //            break;
        //        case "c":
        //            league = 1;
        //            break;
        //    }

        //    var status = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coopname.ToLower());
        //    if(status != null && status.Success) {

        //        var coop = new Coop {
        //            ContractID = guildContract.ContractID,
        //            Created = DateTimeOffset.Now,
        //            GuildId = guildContract.GuildID,
        //            Name = coopname,
        //            MaxUsers = guildContract.Contract.MaxUsers,
        //            Status = CoopStatusEnum.WaitingOnAssigned,
        //            League = (uint)league,
        //            CoopEnds = DateTimeOffset.Now.AddSeconds(status.SecondsRemaining)
        //        };
        //        db.Coops.Add(coop);
        //        await db.SaveChangesAsync();
        //        await command.RespondAsync($"Co-op Added: {coopname} for {((SocketTextChannel)contractchannel).Mention}");
        //        return;
        //    } else {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find co-op details, double check co-op name ({coopname}) and correct contract channel ({((SocketTextChannel)contractchannel).Mention}).");
        //        return;
        //    }
        //}



        [SlashCommand(Description = "Fix a users reference in a co-op when they are showing as an alien", AdminOnly = true)]
        public static async Task FixReference(FauxCommand command, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ApplicationDbContext db, [SlashParam] SocketGuildUser targetuser, [SlashParam(Description = "Egg Inc Name, will match partial name")] string eggincname) {
            //var targetCoop = await db.Coops.AsQueryable().FirstAsync(x => x.DiscordChannelId == command.Channel.Id);
            var xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id && !x.JoinedCoop);
            if(xref == null) {
                xref = await db.UserCoopXrefs.Include(x => x.Coop).FirstOrDefaultAsync(x => x.User.DiscordId == targetuser.Id && x.Coop.DiscordChannelId == command.Channel.Id);
            }
            if(xref == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - Unable to find user assignment to co-op");
                return;
            }


            var t = xref.Coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserName.ToLower().Contains(eggincname.ToLower()));
            if(t == null) {
                await command.RespondAsync($"⚠️ERROR: Bot error - Unable to find user in co-op. You can use a partial in-game name.");
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
            await coopStatusUpdater.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);

            await command.RespondAsync($"Fixed {targetuser.Mention} reference.");
        }

        [SlashCommand(Description = "Move a user to a co-op.", AdminOnly = true)]
        public static async Task MoveToCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, [SlashParam(AutocompleteHandler = typeof(MoveToCoopCoopNameAutoComplete))] string coopid) {
            var coop = await db.Coops.Include(x => x.Contract).FirstOrDefaultAsync(x => x.Id == Guid.Parse(coopid));
            var userid = useraccount.Split("|")[0];
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = dbuser.EggIncAccounts.FirstOrDefault(x => x.Id == useraccount.Split("|")[1]);



            var discordUser = _client.GetUser(dbuser.DiscordId);
            var coopChannel = _client.GetChannel(coop.DiscordChannelId);

            var newxref = await CreateCoopsV2.MoveUser(coop, dbuser.Id, account.Id, account.Name, discordUser, dbuser, (SocketTextChannel)coopChannel, (SocketTextChannel)command.Channel);

            if(newxref == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(coop.GuildId != coop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
                return;
            }

            db.Add(newxref);

            await command.RespondAsync($"Moved {discordUser.Mention} ({account.Name}) to {((ITextChannel)coopChannel).Mention}");
            await db.SaveChangesAsync();
        }


        public class MoveToCoopCoopNameAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public MoveToCoopCoopNameAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var guild = await _db.Guilds.FirstAsync(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                List<CoopMin> coops = null;
                if(string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    coops = await _db.Coops.Include(x => x.Contract)
                        .Where(x => x.DiscordChannelId == arg.ChannelId)
                        .Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract.Name, League = x.League }).ToListAsync();
                }

                if(coops is null) {
                    coops = await _db.Coops.Include(x => x.Contract)
                        .Where(x => EF.Functions.Like(x.Name, $"{(string)arg.Data.Current.Value}%") && !x.DeletedChannel && x.GuildId == guild.Id)
                        .Take(25).Select(x => new CoopMin { Name = x.Name, Id = x.Id, Contract = x.Contract.Name, League = x.League }).ToListAsync();
                }



                await arg.RespondAsync(null, coops.Select(c => new AutocompleteResult($"{c.Name} - {c.Contract} - {PlayerGradeDetails.GetNameFromLeague(c.League)}", c.Id.ToString())).ToArray());
            }

            public class CoopMin {
                public string Name { get; set; }
                public Guid Id { get; set; }
                public string Contract { get; set; }
                public uint League { get; set; }
            }
        }
        public class UserAccountAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public UserAccountAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var guild = await _db.Guilds.FirstAsync(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                var users = await _db.DBUsers
                    .Where(x => x.GuildId == guild.Id && EF.Functions.Like(x.DiscordUsername, $"%{(string)arg.Data.Current.Value}%"))
                    .Take(10).ToListAsync();

                var accounts = users.SelectMany(x => x.EggIncAccounts.Select(y => new { User = x, Account = y }));

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts) {
                    if(account.User.EggIncAccounts.Count > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Name}", $"{account.User.Id}|{account.Account.Id}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.Account.Id}"));
                    }
                }

                await arg.RespondAsync(null, results.ToArray());
            }
        }

        public class UserAccountChannelSpecificAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public UserAccountChannelSpecificAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var coop = await _db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).FirstOrDefaultAsync(x => x.DiscordChannelId == arg.Channel.Id);

                var eidsIn = coop.UserCoopsXrefs.Select(x => x.EggIncId).ToList();
                if(coop is null || coop.FinishedOrFailedOrExpired || eidsIn.Count == 0) {
                    return; //Needs to be used in an active coop channel with users in it
                }

                //Filter users by current search
                var users = string.IsNullOrWhiteSpace((string)arg.Data.Current.Value) ?
                    coop.UserCoopsXrefs :
                    coop.UserCoopsXrefs.Where(x => x.User.DiscordUsername.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase));


                var accounts = users.SelectMany(x => x.User.EggIncAccounts.Where(a => eidsIn.Contains(a.Id)).Select(y => new { User = x.User, Account = y }));

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts) {
                    if(account.User.EggIncAccounts.Count > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Name}", $"{account.User.Id}|{account.Account.Id}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.Account.Id}"));
                    }
                }

                await arg.RespondAsync(null, results.ToArray());
            }
        }


        [SlashCommand(Description = "Remove user from co-op (only works if the bot doesn't see them as joined)", AdminOnly = true)]
        public static async Task RemoveFromCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(AutocompleteHandler = typeof(RemoveFromCoopAutoComplete))] string useraccount) {

            await command.RespondAsync("Please wait...");
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Please use in a co-op channel");
                return;
            }

            var userid = Guid.Parse(useraccount.Split("|")[0]);

            var xref = await db.UserCoopXrefs.Include(x => x.User).Where(xref => xref.UserId == userid && xref.CoopId == targetCoop.Id).OrderBy(x => x.JoinedCoop).FirstOrDefaultAsync();

            if(xref == null) {
                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unabled to find user in co-op");
                return;
            }

            db.Remove(xref);
            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(x => x.Content = $"Removed <@{xref.User.DiscordId}> from co-op");

        }

        public class RemoveFromCoopAutoComplete : AutoCompleteHandler {

            private ApplicationDbContext _db;

            public RemoveFromCoopAutoComplete(ApplicationDbContext db) {
                _db = db;
            }

            public async Task Run(SocketAutocompleteInteraction arg) {
                var users = await _db.UserCoopXrefs.Where(x => x.Coop.DiscordChannelId == arg.Channel.Id).Select(x => new { x.UserId, x.EggIncId, x.User.DiscordUsername }).ToListAsync();
                if(users.Count == 0) {
                    await arg.RespondAsync("Command only works in a co-op channel and where users are assigned.");
                }

                if(!string.IsNullOrWhiteSpace((string)arg.Data.Current.Value)) {
                    users = users.Where(x => x.DiscordUsername.Contains((string)arg.Data.Current.Value, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                await arg.RespondAsync(users.Select(x => new AutocompleteResult(x.DiscordUsername, x.UserId.ToString())));
            }
        }

        [SlashCommand(Description = "Delete a contract channel (Please use this instead of deleting the channel in discord)", AdminOnly = true)]
        public static async Task DeleteContract(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
            var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
            if(guildContract == null) {
                await command.RespondAsync($"⚠️ERROR: Unable to find contract, use only in contract channels.");
                return;
            }
            guildContract.DeletedChannel = true;
            await db.SaveChangesAsync();
            var channel = (SocketTextChannel)command.Channel;
            await channel.DeleteAsync();
        }

        [SlashCommand(Description = "Create a co-op with the selected contract for you")]
        public static async Task CreateCoop(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, Words _words, IServiceProvider _provider, CoopStatusUpdater coopStatusUpdater, [SlashParam(AutocompleteHandler = typeof(CreateCoopContractAutoComplete))] string contractid) {
            await command.RespondAsync("Working...", ephemeral: true);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user is null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find user");
                return;
            }
            var contract = await db.Contracts.FirstAsync(x => x.ID == contractid);

            var subscriptionAccountsCount = user.EggIncAccounts.Where(x => x.SubscriptionLevel > 0).Count();

            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            if(user.EggIncAccounts.Count == 1 || (contract.cc_only && subscriptionAccountsCount == 1)) {

                EggIncAccount subAccountBypass = null;
                if(contract.cc_only) {
                    subAccountBypass = user.EggIncAccounts.FirstOrDefault(x => x.SubscriptionLevel > 0);
                }

                var userList = new List<UserByAccount> { new UserByAccount {
                    Account = subAccountBypass ?? user.EggIncAccounts.First(),
                    User = user
                } };
                var guild = _client.GetGuild(command.GuildId.Value);
                var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue);
                await command.ModifyOriginalResponseAsync("Done");
                await command.Channel.SendMessageAsync($"Co-op created {coop.Name} {PlayerGradeDetails.GetEmoji(coop.League)} for {command.User.Mention}");
            } else {
                var builder = new ComponentBuilder();
                var userList = user.EggIncAccounts;
                if(contract.cc_only) {
                    userList = userList.Where(x => x.SubscriptionLevel > 0).ToList();
                }

                foreach(var account in userList) {
                    Emote.TryParse(PlayerGradeDetails.GetEmoji(account.LastGrade), out var emote);
                    builder.WithButton($"{account.Name} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"CreateCoopButton:{contractid}|{account.Id}", emote: emote);
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to create the co-op with."; x.Components = builder.Build(); });
            }
        }
        [ComponentCommand]
        public static async Task CreateCoopButton(SocketMessageComponent component, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            await component.UpdateAsync(x => { x.Content = "Working..."; x.Components = null; });
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            var contractid = data.Split("|")[0];
            var contract = await db.Contracts.FirstAsync(x => x.ID == contractid);
            var dbguild = await db.Guilds.FirstAsync(x => x.Id == user.GuildId);
            var account = user.EggIncAccounts.First(x => x.Id == data.Split("|")[1]);
            var userList = new List<UserByAccount> { new UserByAccount {
                    Account = account,
                    User = user
                } };
            var guild = _client.GetGuild(component.GuildId.Value);
            var coop = await CreateCoopsV2.Start(userList, contract, userList.First().Account.LastGrade, guild, _words, _provider, dbguild, uint.MaxValue);
            await component.ModifyOriginalResponseAsync(x => x.Content = "Done");
            await component.Channel.SendMessageAsync($"Co-op created {coop.Name} {PlayerGradeDetails.GetEmoji(coop.League)} for {component.User.Mention}");
        }


        public class ContractAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public ContractAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var dbUser = _db.DBUsers.FirstOrDefault(x => x.DiscordId == arg.User.Id);
                var hasSubscriptionAccounts = dbUser.EggIncAccounts.Where(x => x.SubscriptionLevel > 0).Any();

                var contracts = await _db.Contracts.Where(x => hasSubscriptionAccounts ? (x.GoodUntil > DateTimeOffset.Now) : (x.GoodUntil > DateTimeOffset.Now && !x.cc_only)).Select(x => new { x.ID, x.Name }).ToListAsync();

                await arg.RespondAsync(null, contracts.Select(c => new AutocompleteResult(c.Name, c.ID)).ToArray());
            }
        }

        public class CreateCoopContractAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public CreateCoopContractAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var guild = _db.Guilds.FirstOrDefault(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                var dbUser = _db.DBUsers.FirstOrDefault(x => x.DiscordId == arg.User.Id);
                var hasSubscriptionAccounts = dbUser.EggIncAccounts.Where(x => x.SubscriptionLevel > 0).Any();

                var contracts = _db.Contracts.Where(x => hasSubscriptionAccounts ? (x.GoodUntil > DateTimeOffset.Now) : (x.GoodUntil > DateTimeOffset.Now && !x.cc_only)).ToList();

                if(guild is not null && !guild.DisableBG) {
                    //Limit contracts to those that have had longer than 16 hours to launch (i.e. all three boarding groups)
                    contracts = contracts.Where(x => (DateTimeOffset.Now - x.Created).TotalHours > 17).ToList();
                }

                var contractObjs = contracts.Select(x => new { x.ID, x.Name }).ToList();

                await arg.RespondAsync(null, contractObjs.Select(c => new AutocompleteResult(c.Name, c.ID)).ToArray());
            }
        }

        public class MoveGradeAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public MoveGradeAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var coop = await _db.Coops.FirstOrDefaultAsync(x => x.DiscordChannelId == arg.Channel.Id);

                if(coop is null || coop.League == 0) {
                    return; //Command only works in a co-op channel and where grade is known.
                }

                var result = Enumerable.Range(1, 5)
                    .Where(i => i != coop.League && Math.Abs(coop.League - i) < 2)
                    .Reverse()
                    .ToList()
                    .Select(x => new AutocompleteResult(PlayerGradeDetails.GetText((PlayerGrade)x), (uint)x));

                await arg.RespondAsync(
                    result
                );
            }
        }

        //[SlashCommand(Description = "Makes it so the bot won't notify you for contracts that don't have an Egg of Prophecy (PE)")]
        //public static async Task SkipNoEggOfProphecy(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
        //    if(dbUser == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find user");
        //        return;
        //    }

        //    dbUser.SkipNoPE = true;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"You are set to skip contracts without <:Egg_of_Prophecy_PE:669981330477547580>, what this means is you won't get a demerit for not participating in this contract. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server.", ephemeral: true);
        //}

        //[SlashCommand(Description = "Bot will notify you of contracts even without an Egg of Prophecy (PE)")]
        //public static async Task UnSkipNoPeEggOfProphecy(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    var dbUser = db.DBUsers.FirstOrDefault(x => x.DiscordId == command.User.Id);
        //    if(dbUser == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find user");
        //        return;
        //    }

        //    dbUser.SkipNoPE = false;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"You are NO longer set to skip contracts without an <:Egg_of_Prophecy_PE:669981330477547580>.", ephemeral: true);
        //}

        //[SlashCommand(Description = "Stop the bot from pinging you for the tagged contract.")]
        //public static async Task Skip(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam] SocketChannel contractchannel) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == contractchannel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, have you tagged a contract channel?");
        //        return;
        //    }

        //    var skipList = JsonConvert.DeserializeObject<List<ulong>>(guildContract.Skip ?? "[]");
        //    skipList.Add(command.User.Id);

        //    guildContract.Skip = JsonConvert.SerializeObject(skipList);
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"{command.User.Mention} is set to skip {((SocketTextChannel)contractchannel).Mention}. **If you have already started the contract and don't exit it, you will still get a demerit**. If you change your mind, just start pre-farming and you will show up in a co-op. **What this doesn't mean** is that you can participate in an outside co-op. To do that you need to leave the server.", ephemeral: true);
        //}

        private static Dictionary<ulong, SemaphoreSlim> startSemapohores = new Dictionary<ulong, SemaphoreSlim>();
        private static SemaphoreSlim dictionarySemaphore = new SemaphoreSlim(1);

        private static async Task<SemaphoreSlim> AwaitStartSemaphore(FauxCommand command) {
            await dictionarySemaphore.WaitAsync();
            SemaphoreSlim semaphore;
            if(startSemapohores.ContainsKey(command.ChannelId.Value)) {
                semaphore = startSemapohores[command.ChannelId.Value];
            } else {
                semaphore = new SemaphoreSlim(1);
                startSemapohores.Add(command.ChannelId.Value, semaphore);
            }
            dictionarySemaphore.Release();
            await semaphore.WaitAsync();
            return semaphore;
        }

        //private static async Task _start(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, int percent, APILink _apiLink, Words _words) {
        //    var semapohre = await AwaitStartSemaphore(command);
        //    try {
        //        var coopCount = 0;

        //        await command.RespondAsync($"Working...");
        //        var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //        if(guildContract == null) {
        //            await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //            return;
        //        }

        //        var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        foreach(var coopDetail in coopsBreakdown.PotentialCoops) {
        //            var targetAmount = guildContract.Contract.Details.GoalSets[(int)guildContract.League].Goals.Last().TargetAmount;
        //            var cooppercent = (decimal)(coopDetail.CoopParticipants.Sum(x => x.Projected) / targetAmount) * 100m;
        //            if(cooppercent < percent) {
        //                continue;
        //            }

        //            var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);
        //            var coop = await CreateCoops.Start(coopDetail.CoopParticipants, guildContract, guild, _words, db);

        //            coopCount++;
        //        }

        //        if(percent == 0 || coopCount == coopsBreakdown.PotentialCoops.Count) {
        //            guildContract.Status = ContractStatus.Creating;
        //        }


        //        guildContract.NumberOfCoops = Math.Max(0, guildContract.NumberOfCoops - coopCount);
        //        await db.SaveChangesAsync();
        //        await command.Channel.SendMessageAsync($"Started {coopCount} co-ops");
        //        coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        //await ContractUpdater.UpdateContractChannelName(guildContract, coopsBreakdown, (SocketTextChannel)command.Channel);
        //    } finally {
        //        semapohre.Release();
        //    }
        //}

        //[SlashCommand(Description = "Start a user's co-op", AdminOnly = true)]
        //public static async Task StartUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] SocketGuildUser user, [SlashParam(Description = "Fill the co-op with other users")] bool fillcoop) {
        //    await _StartUser(command, db, _client, _apiLink, _words, fillcoop, user);
        //}


        //[SlashCommand(Description = "Start co-ops with users above a certain percent and backfill with low EB users", AdminOnly = true)]
        //public static async Task StartFill(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam(Description = "Percent at which a user will be started")] int percenttostart) {
        //    if(percenttostart < 120) {
        //        await command.RespondAsync($"Minumum percent for </startfill:1095116344392941610> is 120%");
        //        return;
        //    }
        //    await _StartUser(command, db, _client, _apiLink, _words, true, percent: percenttostart);
        //}

        //[SlashCommand(Description = "Start fire co-op and backfill with low EB users", AdminOnly = true)]
        //public static async Task StartFire(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
        //    await _StartUser(command, db, _client, _apiLink, _words, true, startFire: true);
        //}




        //public static async Task _StartUser(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, bool fill, SocketGuildUser user = null, int? percent = null, bool startFire = false) {
        //    var semapohre = await AwaitStartSemaphore(command);
        //    try {

        //        var coopCount = 0;

        //        var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //        if(guildContract == null) {
        //            await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //            return;
        //        }

        //        await command.RespondAsync($"Working...");


        //        var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);




        //        var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        var prefarms = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Where(x => x.ProjectedPercent < 10).OrderByDescending(x => x.Backup.EarningsBonus).ToList();

        //        var targetAmount = guildContract.Contract.Details.GoalSets[(int)guildContract.League].Goals.Last().TargetAmount;


        //        if(user == null) {
        //            List<CoopDetails> coopsToStart;
        //            var onlyCarries = coopsBreakdown.PotentialCoops.Select(c => {
        //                var carry = c.CoopParticipants.OrderByDescending(x => x.Projected).First();
        //                return new CoopDetails(c.CoopParticipants.Where(x => x.DBUser.Id == carry.DBUser.Id).ToList(), guildContract);
        //            });
        //            if(startFire) {
        //                coopsToStart = onlyCarries.Where(x => x.IsFire || x.IsDoubleFire).ToList();
        //            } else {
        //                coopsToStart = onlyCarries.Where(x => x.PercentProjected >= percent).ToList();
        //            }
        //            foreach(var coop in coopsToStart) {
        //                //var carry = coop.CoopParticipants.OrderByDescending(x => x.Projected).First();
        //                //var carryWithAlts = coop.CoopParticipants.Where(x => x.DiscordUser.Id == carry.DiscordUser.Id).ToList();
        //                var participants = await _startUserCreateCoop(coop.CoopParticipants, guildContract, prefarms, db, guild, _words, true);
        //                var numberRemoved = prefarms.RemoveAll(x => participants.Any(y => y == x));
        //                coopCount++;
        //            }
        //            //} else if(user == null) {
        //            //    var usersAbovePercent = prefarms.Where(p => (p.Projected / targetAmount) * 100 >= percent).ToList();
        //            //    usersAbovePercent.ForEach(x => prefarms.Remove(x));
        //            //    prefarms = prefarms.Where(p => (p.Projected / targetAmount) * 100 < 5).ToList();
        //            //    foreach(var userAbovePercent in usersAbovePercent) {
        //            //        var participants = await _startUserCreateCoop(userAbovePercent, guildContract, prefarms, db, guild, _words);
        //            //        participants.ForEach(x => prefarms.Remove(x));
        //            //        coopCount++;
        //            //    }
        //        } else {
        //            var prefarm = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants.Where(y => y.DiscordUser.Id == user.Id)).First();
        //            prefarms.Remove(prefarm);
        //            _ = await _startUserCreateCoop(new List<UserFarmDetails> { prefarm }, guildContract, prefarms, db, guild, _words, fill);
        //            coopCount++;
        //        }

        //        guildContract.NumberOfCoops = Math.Max(1, guildContract.NumberOfCoops - coopCount);
        //        await db.SaveChangesAsync();


        //        try {
        //            await command.Channel.SendMessageAsync($"Finished Starting {coopCount} coop{(coopCount > 1 ? "s" : "")}");
        //        } catch(Exception) {
        //            //Possible message was deleted in the meantime
        //        }

        //        coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        //await ContractUpdater.UpdateContractChannelName(guildContract, coopsBreakdown, (SocketTextChannel)command.Channel);
        //    } finally {
        //        semapohre.Release();
        //    }
        //}

        //private static async Task<List<UserFarmDetails>> _startUserCreateCoop(List<UserFarmDetails> existingUsers, GuildContract guildContract, List<UserFarmDetails> otherUsers, ApplicationDbContext db, SocketGuild guild, Words _words, bool fill = true) {
        //    var participants = new List<UserFarmDetails>();
        //    //if(guildContract.Contract.MaxUsers > 4) {
        //    //    participants.AddRange(prefarms.Where(x => x.DiscordId == user.DiscordId));
        //    //}
        //    participants.AddRange(existingUsers);
        //    if(fill) {
        //        participants.AddRange(otherUsers.TakeLast(guildContract.Contract.MaxUsers - participants.Count));
        //    }

        //    var coop = await CreateCoops.Start(participants, guildContract, guild, _words, db);
        //    return participants;
        //}


        //[SlashCommand(Description = "Start an empty co-op", AdminOnly = true)]
        //public static async Task StartEmpty(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"Unable to find contract, is this run in a contract channel?");
        //        return;
        //    }
        //    var guild = _client.Guilds.First(x => x.Id == guildContract.GuildID);

        //    var coop = await CreateCoops.Start(new List<UserFarmDetails>(), guildContract, guild, _words, db);
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"Empty co-op created");
        //    return;
        //}

        //[SlashCommand(Description = "Start all co-ops as-is that are above a certain percent", AdminOnly = true)]
        //public static async Task StartPercent(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words, [SlashParam] int percent) {
        //    await _start(command, db, _client, percent, _apiLink, _words);
        //}

        //[SlashCommand(Description = "Start all co-ops", AdminOnly = true)]
        //public static async Task StartAll(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, APILink _apiLink, Words _words) {
        //    await _start(command, db, _client, 0, _apiLink, _words);
        //}


        //public static async Task SetNumber(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //        return;

        //    }

        //    if(args.Length == 0) {
        //        await command.RespondAsync($"⚠️ERROR: Please include the number of coops you would like the bot to create");
        //        return;
        //    }

        //    var number = Int32.Parse(args[0]);

        //    guildContract.NumberOfCoops = number;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"# of coops set to {number}");
        //}

        //[SlashCommand(Description = "Set the number of potential co-ops", AdminOnly = true)]
        //public static async Task SetNumber(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client, [SlashParam(Description = "Number of potental co-ops (excludes ones already created)")] int numberofcoops) {
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //        return;

        //    }


        //    guildContract.NumberOfCoops = numberofcoops;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"# of coops set to {numberofcoops}");
        //    //ResetUpdateTimer();
        //}

        //public static async Task Update(FauxCommand command, ApplicationDbContext db, DiscordSocketClient _client) {
        //    await command.RespondAsync($"Updating...");
        //    ContractUpdater.ResetTimeStatic();
        //}



        //private static readonly AsyncLock joinLock = new AsyncLock();
        //        [SlashCommand(Description = "Join a Co-op", ParentCommand = "a", AdminOnly = true, AllowFarmHand = true)]
        //public static async Task Join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam] SocketGuildUser targetUser) {
        //    await _join(command, db, _apiLink, _client, targetUser);
        //}
        //[SlashCommand(Description = "Join a Co-op (Only usable in a #spots thread)")]
        //public static async Task Join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client) {
        //    await _join(command, db, _apiLink, _client, command.User);
        //}
        //public static async Task _join(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, IUser targetUser) {

        //    await command.RespondAsync("Please wait finding a co-op...");
        //    using(await joinLock.LockAsync()) {
        //        var dbuser = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == targetUser.Id);
        //        SocketThreadChannel thread;
        //        try {
        //            thread = (SocketThreadChannel)command.Channel;
        //        } catch(Exception ex) when(ex is AggregateException || ex is InvalidCastException) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = "⚠️ERROR: Unable to find contract details, this command only works in a contract spots thread.");
        //            return;
        //        }
        //        var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
        //        if(guildContract == null) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
        //            return;
        //        }
        //        var targetAmount = guildContract.Contract.Details.GoalSets[(int)guildContract.League].Goals.Last().TargetAmount;

        //        var contractBreakdown = await GetBreakdown(db, guildContract, _client);

        //        var currentCoops = contractBreakdown.ExistingCoops.Where(x => !x.Coop.Finished && x.HasSpots && x.TimeRemaining > TimeSpan.Zero).OrderByDescending(x => x.HasSpots).ThenBy(x => x.TimeRemaining).ToList();

        //        if(currentCoops.Count == 0) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find open co-op, all spots may have been filled.");
        //            return;
        //        }


        //        Coop targetCoop = null;
        //        SocketTextChannel targetChannel = null;

        //        foreach(var coop in currentCoops) {
        //            var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
        //            if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize) {
        //                targetChannel = (SocketTextChannel)_client.GetChannel(coop.Coop.DiscordChannelId);
        //                if(targetChannel != null) {
        //                    targetCoop = coop.Coop;
        //                    break;
        //                }
        //            }

        //        }
        //        if(targetCoop == null) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find open co-op, all spots may have been filled.");
        //            return;

        //        }

        //        var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == dbuser.DiscordId && x.CreatedOn > DateTimeOffset.Now.AddMonths(-2)).ToListAsync();
        //        if(xrefs.Count == dbuser.EggIncAccounts.Count) {
        //            if(xrefs.Count == 1 && (xrefs.First().Coop.DeletedChannel || xrefs.First().Coop.FinishedOrFailed)) {
        //                db.UserCoopXrefs.Remove(xrefs.First());
        //                await db.SaveChangesAsync();
        //                xrefs = new List<UserCoopXref>();
        //            } else {
        //                var coopLinks = string.Join(", ", xrefs.Select(x => $"<https://discord.com/channels/{x.Coop.OverflowGuildId}/{x.Coop.DiscordChannelId}>"));
        //                await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: <@{dbuser.DiscordId}> has already been assigned {(xrefs.Count > 1 ? "the co-ops" : "a co-op")}. You can get to the co-op by clicking the following link {coopLinks}, if this co-op has already finished contact staff and we can get it fixed.");
        //                return;
        //            }
        //        }

        //        string EggIncId;
        //        var eggIncName = "";
        //        if(xrefs.Count == 0 || dbuser.EggIncAccounts.Count > 1) {
        //            if(dbuser.EggIncAccounts.Count > 1) {
        //                var prefarms = contractBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).Where(x => x.DBUser.Id == dbuser.Id);
        //                if(prefarms.Count() == 0) {
        //                    await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Looks like all prefarms for <@{dbuser.DiscordId}> have been assigned.");
        //                    return;
        //                }
        //                EggIncId = prefarms.First().EggIncId;
        //                eggIncName = $" ({prefarms.First().Backup.UserName})";
        //            } else {
        //                EggIncId = dbuser.EggIncAccounts.First().Id;
        //            }
        //        } else {
        //            EggIncId = xrefs.First().EggIncId;
        //        }

        //        var newxref = await CreateCoops.MoveUser(targetCoop, dbuser.Id, EggIncId, eggIncName, targetUser, dbuser, (SocketTextChannel)targetChannel, (SocketTextChannel)command.Channel);

        //        if(newxref == null) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"{command.User.Mention} looks like you are not in the overflow servers. **Make sure and join the overflow servers in <#775558629671698442> to see your co-op, it's in {targetChannel.Guild.Name}**. Once you've joined the overflows use this link to get to your co-op 👉 https://discord.com/channels/{targetCoop.OverflowGuildId}/{targetCoop.DiscordChannelId}");
        //            return;
        //        }

        //        db.RemoveRange(xrefs);
        //        db.Add(newxref);

        //        await db.SaveChangesAsync();
        //        await command.ModifyOriginalResponseAsync(x => x.Content = $"Moved you to a co-op, link to the co-op 👉 https://discord.com/channels/{targetCoop.OverflowGuildId}/{targetCoop.DiscordChannelId}");
        //    }
        //}

        //[SlashCommand(Description = "Move prefarmers to co-ops ending >24h", AdminOnly = true)]
        //public static async Task MovePrefarmers(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam(Required = false)] int overrideperecent = 10, [SlashParam(Required = false, Description = "Fill co-ops regardless of %")] bool fillSmallest = false) {
        //    if(overrideperecent == 0)
        //        overrideperecent = 10;
        //    await command.RespondAsync("Please wait moving prefarmers...");
        //    using(await joinLock.LockAsync()) {
        //        var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == command.Channel.Id);
        //        if(guildContract == null) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find contract details, is this command posted in a contract channel?");
        //            return;
        //        }
        //        var targetAmount = guildContract.Contract.Details.GoalSets[(int)guildContract.League].Goals.Last().TargetAmount;

        //        var coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        var currentCoops = coopsBreakdown.ExistingCoops.Where(x => x.TimeRemaining > TimeSpan.FromHours(24) && !x.Coop.FinishedOrFailed && x.HasSpots).OrderBy(x => x.TimeRemaining).ToList();


        //        if(currentCoops.Count == 0) {
        //            await command.ModifyOriginalResponseAsync(x => x.Content = $"⚠️ERROR: Unable to find open co-op, all spots may have been filled.");
        //            return;
        //        }

        //        var allPrefarms = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).OrderBy(x => x.Backup.EarningsBonus).ToList();
        //        var discordIds = allPrefarms.Select(p => p.DiscordUser.Id);

        //        if(!fillSmallest)
        //            currentCoops = currentCoops.Where(x => x.PercentProjected > 100).ToList();


        //        var prefarmsAbovePercent = allPrefarms.Where(p => p.ProjectedPercent >= overrideperecent);

        //        if(prefarmsAbovePercent.Any()) {
        //            await DiscordMessageSplitter.SendMessageSplitAsync(
        //                command.Channel,
        //                $"The following users are above {overrideperecent}% and won't be moved: \n{string.Join("\n", prefarmsAbovePercent.OrderBy(x => x.Projected).Select(p => $"{p.Name} {Math.Round(p.ProjectedPercent)}%"))}",
        //                "\n"
        //                );
        //        }

        //        allPrefarms = allPrefarms.Where(p => p.ProjectedPercent < overrideperecent).ToList();

        //        foreach(var user in allPrefarms) {
        //            var discordUser = _client.GetUser(user.DBUser.DiscordId);
        //            var added = false;
        //            if(fillSmallest)
        //                currentCoops = currentCoops.OrderBy(x => x.CoopParticipants.Count).ToList();
        //            foreach(var coop in currentCoops.Where(x => x.HasSpots)) {
        //                var coopStatus = await ContractsAPI.GetCoopStatus(guildContract.ContractID, coop.Coop.Name.ToLower());
        //                if(coopStatus.Contributors.Count < guildContract.Contract.Details.MaxCoopSize) {
        //                    var targetCoop = coop.Coop;
        //                    var xref = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.EggIncId == user.EggIncId && x.Coop.ContractID == targetCoop.ContractID && x.User.DiscordId == user.DBUser.DiscordId).FirstOrDefaultAsync();
        //                    if(xref?.JoinedCoop ?? false) {
        //                        await command.Channel.SendMessageAsync($"⚠️ERROR: Unable to add {user.Name}, they are already assigned and joined a co-op");
        //                        break;
        //                    }

        //                    var eggIncName = "";
        //                    if(user.DBUser.EggIncAccounts.Count > 1 && !string.IsNullOrEmpty(user.Backup.UserName)) {
        //                        eggIncName = $" ({user.Backup.UserName})";
        //                    }

        //                    var channel = (SocketTextChannel)_client.GetChannel(targetCoop.DiscordChannelId);
        //                    var newxref = await CreateCoops.MoveUser(targetCoop, user.DBUser.Id, user.EggIncId, eggIncName, discordUser, user.DBUser, (SocketTextChannel)channel, (SocketTextChannel)command.Channel);

        //                    if(newxref == null) {
        //                        await command.Channel.SendMessageAsync($"⚠️ERROR: Unable to add permission for {discordUser.Mention}{(targetCoop.GuildId != targetCoop.OverflowGuildId ? ", possibly not in overflow server" : "")}");
        //                        continue;
        //                    }


        //                    if(xref is not null) {
        //                        db.Remove(xref);
        //                    }
        //                    db.Add(newxref);

        //                    await db.SaveChangesAsync();
        //                    await command.Channel.SendMessageAsync($"Moved {user.Name} to a co-op");
        //                    added = true;
        //                    coop.CoopParticipants.Add(user);
        //                    break;
        //                }

        //            }
        //            if(!added) {
        //                await command.Channel.SendMessageAsync($"⚠️ERROR: Unable to find open co-op, all spots may have been filled.");
        //                break;

        //            }

        //        }
        //        await command.DeleteResponseFix();

        //        coopsBreakdown = await Prefarm.GetBreakdown(db, guildContract, _client);

        //        //await ContractUpdater.UpdateContractChannelName(guildContract, coopsBreakdown, (SocketTextChannel)command.Channel);

        //    }
        //}


        //[SlashCommand(Description = "Ping people to add them to a spots thread", AdminOnly = true)]
        //public static async Task SpotPings(FauxCommand command, ApplicationDbContext db, APILink _apiLink, DiscordSocketClient _client, [SlashParam(Required = false)] int overridepercent = 10) {
        //    if(overridepercent == 0)
        //        overridepercent = 10;
        //    if(command.Channel is not SocketThreadChannel) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
        //        return;
        //    }
        //    var thread = (SocketThreadChannel)command.Channel;
        //    var guildContract = db.GuildContracts.Include(x => x.Contract).FirstOrDefault(x => x.DiscordChannelId == thread.CategoryId);
        //    if(guildContract == null) {
        //        await command.RespondAsync($"⚠️ERROR: Unable to find contract details, is this command posted in a contract spots thread?");
        //        return;
        //    }
        //    await command.RespondAsync("Pinging users and removing existing pings that aren't needed", ephemeral: true);
        //    var targetAmount = guildContract.Contract.Details.GoalSets[(int)guildContract.League].Goals.Last().TargetAmount;

        //    var coopsBreakdown = await GetBreakdown(db, guildContract, _client);

        //    var threadMessages = await command.Channel.GetMessagesAsync(limit: 500).FlattenAsync();



        //    var usersWithFarm = new List<UserFarmDetails>();
        //    usersWithFarm.AddRange(coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants));
        //    usersWithFarm.AddRange(coopsBreakdown.ExpiredFarms);

        //    var oldPings = threadMessages.Where(m => m.MentionedUserIds.Count == 1 && usersWithFarm.Any(u => u.DiscordUser.Id == m.MentionedUserIds.First()) && m.CreatedAt.AddHours(12) < DateTimeOffset.Now);



        //    var usersToPing = usersWithFarm.Where(u => u.ProjectedPercent < overridepercent && !threadMessages.Any(m => m.MentionedUserIds.Any(mu => mu == u.DiscordUser.Id))).ToList();
        //    var pingsToRemove = threadMessages.Where(m =>
        //     (m.MentionedUserIds.Count == 1 && !usersWithFarm.Any(u => u.DiscordUser.Id == m.MentionedUserIds.First()))
        //    //|| (!usersWithFarm.Any(u => m.Author.Id == u.DiscordId) && !m.Author.IsBot && !m.Author.rol)

        //    ).ToList();


        //    var joinCommands = threadMessages.Where(m => (m.Interaction != null && m.Interaction.Type == InteractionType.ApplicationCommand) || m.Content == "/join");
        //    var usersAbovePercent = usersWithFarm.Where(u => u.ProjectedPercent >= overridepercent).ToList().OrderBy(x => x.Projected);

        //    foreach(var user in usersToPing) {
        //        await command.Channel.SendMessageAsync($"<@{user.DiscordUser.Id}>");
        //    }

        //    var messagesToDelete = new List<IMessage>();

        //    foreach(var ping in pingsToRemove) {
        //        messagesToDelete.Add(ping);
        //    }

        //    foreach(var oldPing in oldPings) {
        //        var user = usersWithFarm.First(x => x.DiscordUser.Id == oldPing.MentionedUserIds.First());
        //        if(user.ProjectedPercent < overridepercent) {
        //            await command.Channel.SendMessageAsync($"<@{oldPing.MentionedUserIds.First()}>");
        //        }
        //        messagesToDelete.Add(oldPing);
        //        var discordUser = await (command.Channel as ITextChannel).Guild.GetUserAsync(user.DiscordUser.Id);

        //        if(usersAbovePercent.Any(y => y.DiscordUser.Id == oldPing.MentionedUserIds.First())) {
        //            await (command.Channel as IThreadChannel).RemoveUserAsync(discordUser);
        //        }
        //    }


        //    foreach(var commandMessage in joinCommands) {
        //        if(commandMessage.Reference != null) {
        //            var referenceMessages = threadMessages.Where(x => x.Id == commandMessage.Reference.MessageId.Value);
        //            foreach(var referenceMessage in referenceMessages) {
        //                await referenceMessage.DeleteAsync();
        //            }
        //        }
        //        messagesToDelete.Add(commandMessage);
        //    }

        //    messagesToDelete = messagesToDelete.Distinct().ToList();
        //    foreach(var group in
        //        messagesToDelete.Where(x => x.Type != MessageType.RecipientRemove).Select((x, i) => new { Index = i, Value = x })
        //            .GroupBy(x => x.Index / 20)
        //            .Select(x => x.Select(v => v.Value).ToList())) {
        //        await thread.DeleteMessagesBatchAsync(group);
        //    }


        //    //await command.DeleteResponseFix();
        //    if(usersAbovePercent.Count() > 0) {
        //        var usersString = string.Join("\n", usersAbovePercent.Select(x => {
        //            string expireMessage = "";
        //            if(x.TimeLeft < TimeSpan.Zero) {
        //                expireMessage = $"Expired {x.TimeLeft.Humanize()} ago";
        //            } else if(x.TimeLeft < TimeSpan.FromDays(1)) {
        //                expireMessage = $"Expires in {x.TimeLeft.Humanize()}";
        //            }
        //            return $"{x.DBUser.DiscordUsername} {Math.Round(x.ProjectedPercent)}% {expireMessage}";
        //        }));
        //        await command.ModifyOriginalResponseAsync(x => x.Content = $"Did not add the following: \n {usersString}");
        //    } else {
        //        await command.ModifyOriginalResponseAsync(x => x.Content = $"Pings added: {usersToPing.Count()}");
        //    }

        //}

    }
}


