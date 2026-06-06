using Discord;
using Discord.WebSocket;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class UserStatusCommands {

        [SlashCommand(Description = "Get a users status", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, [SlashParam] SocketUser user, [SlashParam(Required = false)] bool showinchannel = false,
            [SlashParam(Required = false, Description = "Pull a fresh backup for all accounts of this user before reporting their status")] bool pullfreshbackup = false) {
            return _userstatus(command, db, _client, user, true, showinchannel, pullfreshbackup);
        }

        [SlashCommand(Description = "Get your status", AllowInDMs = true)]
        public static Task UserStatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client) {
            return _userstatus(command, db, _client, command.User);
        }

        public static async Task _userstatus(FauxCommand command, ApplicationDbContext db, DiscordHostedService _client, IUser user, bool admin = false, bool showInChannel = false, bool pullFreshBackup = false) {
            await command.DeferAsync(ephemeral: !showInChannel);
            var dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == user.Id);
            if(dbuser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{user.Id}>"); });
                return;
            }
            if(dbuser.EggIncAccounts == null || dbuser.EggIncAccounts.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No registered accounts found for <@{user.Id}>"); });
                return;
            }

            if(pullFreshBackup) {
                foreach(var account in dbuser.EggIncAccounts) {
                    var rawBackup = await EggIncApi.FirstContact(account.Id);
                    if(rawBackup is null || rawBackup.Backup is null) {
                        await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Backup for account `{account?.Backup?.UserName ?? account.Id}` returned as null from the API"); });
                        return;
                    }
                    var customBackup = new CustomBackup(rawBackup.Backup, await db.CachedEiContractsAsync(), account?.Backup ?? null);
                    if(customBackup?.Farms is not null) {
                        account.Backup = customBackup;
                    }
                }
                dbuser.UpdateAccounts();
                await db.SaveChangesAsync();
            }

            var guild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == dbuser.GuildId);
            var builders = await AccountsString(db, dbuser, admin);
            var lastBuilder = builders.LastOrDefault();
            if(lastBuilder.Footer == null) lastBuilder.WithFooter("");

            if(dbuser.TempDisabled) lastBuilder.Footer.Text += $"\n❗User is disabled";

            if(command.Channel is SocketDMChannel) {
                if(dbuser.GuildId > 0) {
                    lastBuilder.Footer.Text += $"\nRegistered with the server {_client.GetGuild(dbuser.GuildId).Name}";
                } else {
                    lastBuilder.Footer.Text += $"\nNot registered with a guild";
                }
            } else if(guild is not null && dbuser.GuildId == guild.Id && !dbuser.TempDisabled) {
                lastBuilder.Footer.Text += $"\nProperly registered with this server";
            } else if(guild is null || dbuser.GuildId != guild.Id) {
                lastBuilder.Footer.Text += $"\nNot registered with this server, try the /moveserver command";
            }

            if(dbuser.Registered.HasValue) {
                lastBuilder.Footer.Text += $"\nJoined the bot on {dbuser.Registered.Value:MMM dd, yyyy}";
            } else {
                lastBuilder.Footer.Text += $"\nMissing bot registration date";
            }

            if(guild is not null && dbuser.GuildId > 0 && !dbuser.TempDisabled && user is SocketGuildUser guildUser && guild.Id == guildUser.Guild.Id) {
                _ = await DiscordHelpers.CheckRoles(db, _client.GetGuild(dbuser.GuildId), guildUser, dbuser, _client, null, []);
            }

            await command.RespondAsync("", embeds: [.. builders.Select(builder => builder.Build())], ephemeral: !showInChannel);
        }

        static async internal Task<List<EmbedBuilder>> AccountsString(ApplicationDbContext db, DBUser user, bool admin) {
            var dbguild = await db.Guilds.FirstOrDefaultAsync(x => x.Id == user.GuildId);
            var builderList = new List<EmbedBuilder>();
            var footers = new List<string>();
            var builder = new EmbedBuilder {
                Title = "User Status",
                Url = (admin ? $"https://egg9000.com/MyFarms/ViewUser?discordId={user.DiscordId}" : "")
            };

            var accounts = user.EggIncAccounts.OrderByDescending(u => u.Backup?.EarningsBonus);

            foreach(var (account, index) in accounts.Select((value, i) => (value, i))) {
                if(index % 2 == 0 && index != 0) {
                    builderList.Add(builder);
                    builder = new EmbedBuilder();
                }

                var backup = await EggIncApi.GetBackupAsync(account.Id, await db.CachedEiContractsAsync());
                if(backup == null)
                    continue;

                var deviceTypeEmoji = account.DeviceID is not null ? (account.DeviceID.Length == 16 ? ":robot: " : ":apple: ") : "";
                var permitEmoji = account.Backup is not null ? (account.Backup?.PermitLevel == 0 ? "<:Standard_Permit:755734059761795173> " : "<:Pro_Permit:724392625276452955> ") : "";
                var subscriptionEmoji = account.HasActiveSubscription() ? "<:ultra:1131045418319495369> " : "";

                builder.AddField("――――――――――――――――――", ($"{deviceTypeEmoji}{permitEmoji}{subscriptionEmoji}{((account.GetGrade() != default) ? $"{PlayerGradeDetails.GetEmoji(account.GetGrade())} " : "")}***{account.Backup?.UserName} " ?? "***(No Name)") + (backup?.Farms?.Count > 0 ? $"({backup.EarningsBonus.ToEggString()})***: " : "***: ") + (account.Id ?? "No EID"));
                builder.AddField("Last Backup", (backup?.Farms?.Count > 0) ? DiscordHelpers.TimeStamper(DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime)) : "Empty - Check EID", true);

                if(account.GetGrade() != default) {
                    var pGrade = account.GetGrade();
                    var gradeProgressPercent = Math.Round(account.Backup?.GradeProgress ?? 0 * 100, 2);

                    if(gradeProgressPercent > 0 && pGrade != Ei.Contract.Types.PlayerGrade.GradeAaa) {
                        builder.AddField("Rankup Percentage", $"{gradeProgressPercent}% to {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)((int)pGrade + 1))} :chart_with_upwards_trend:", true);
                    } else if(gradeProgressPercent < 0 && pGrade != Ei.Contract.Types.PlayerGrade.GradeC) {
                        builder.AddField("Rankdown Percentage", $"\n\t{gradeProgressPercent * -1}% to {PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)((int)pGrade - 1))} :chart_with_downwards_trend:", true);
                    }
                }

                if(dbguild is null || !dbguild.DisableBG) {
                    if(account.HasActiveSubscription()) {
                        builder.AddField("Boarding Groups", $"{(account?.Group == 0 ? "**None**" : "BG" + account?.Group)}/{(account?.UltraGroup == 0 ? "**None**" : "UG" + account?.UltraGroup)}", true);
                    } else {
                        builder.AddField("Boarding Group", account?.Group == 0 ? "**None**" : "BG" + account?.Group, true);
                    }
                }

                var filterStr = string.Join(", ", account.AutoRegisterRewards ?? []) ?? "No Filter";
                var breakStr = account.OnBreakUntil == default ? "No" : "On break until <t:" + account.OnBreakUntil.ToUnixTimeSeconds() + ":f>";
                var redoOpt = account.RedoLeggacySelection == default ? RedoLeggacyOption.NotSet : account.RedoLeggacySelection;
                var redoStr = redoOpt == RedoLeggacyOption.YesThreshold ? $"{redoOpt} {((double)account.RedoScoreThreshold).ToEggString()}" : redoOpt.ToString();

                builder.AddField("Filter", filterStr == "" ? "None" : filterStr, true);
                builder.AddField("Break", breakStr == "" ? "No" : breakStr, true);
                builder.AddField("Redo Leggacy", redoStr == "" ? "Not Set" : redoStr, true);

                if(dbguild?.AllowGuilds ?? false) {
                    builder.AddField("Guild", string.IsNullOrWhiteSpace(account.Guild) ? "None" : account.Guild, true);
                }

                if(backup.ClientVersion < EggIncApi.ClientVersion && backup.ClientVersion > 0) {
                    footers.Add($"⚠️ Game outdated for {backup.UserName}, showing {backup.ClientVersion}, new version is {EggIncApi.ClientVersion} ⚠️");
                }

                if(index + 1 == accounts.Count()) {
                    foreach(var footer in footers) {
                        builder.WithFooter(footer);
                    }
                    builderList.Add(builder);
                }
            }

            if(admin) {
                var lastBuilder = builderList.Last();
                var infoSeparatorAdded = false;
                void AddInfoSeparatorIfNeeded() {
                    if(infoSeparatorAdded) return;
                    lastBuilder.AddField("――――――――――――――――――", "User Information");
                    infoSeparatorAdded = true;
                }

                var xrefs = await db.UserCoopXrefs.Include(x => x.Coop).Where(x => x.UserId == user.Id && !x.Coop.ThreadArchived).ToListAsync();
                var xrefsShortened = false;
                if(xrefs.Count > 4) {
                    xrefs = [.. xrefs.OrderByDescending(x => x.CreatedOn).Take(4)];
                    xrefsShortened = true;
                }

                var coopsString = $"{string.Join("\n", xrefs.Select(x => $"<#{x.Coop.ThreadID}> {(user.EggIncAccounts.Count > 1 ? $"({user.EggIncAccounts.FirstOrDefault(y => y.Id == x.EggIncId)?.Backup?.UserName ?? "(No name)"})" : "")}"))}";
                if(coopsString != "") {
                    AddInfoSeparatorIfNeeded();
                    lastBuilder.AddField($"Coops {(xrefsShortened ? "(Shortened List)" : "")}", coopsString);
                }

                var recentDemeritsString = $"{await DemeritCommands.GetDemerits(user.Id, db)}";
                if(recentDemeritsString != "") {
                    AddInfoSeparatorIfNeeded();
                    lastBuilder.AddField("Recent Demerits", recentDemeritsString);
                }

                if(!string.IsNullOrEmpty(user.Notes)) {
                    AddInfoSeparatorIfNeeded();
                    lastBuilder.AddField("Notes", user.Notes);
                }
            }

            return builderList;
        }
    }
}
