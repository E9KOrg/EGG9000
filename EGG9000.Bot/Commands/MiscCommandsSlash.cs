using Discord;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Helpers;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Services;
using EGG9000.Common.Commands;
using EGG9000.Common.Extensions;
using EGG9000.Common.JsonData.EiAfxData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Globalization;
using Google.Protobuf.WellKnownTypes;
using static Ei.Backup.Types;
using static EGG9000.Bot.Commands.ContractCommandsSlash;
using System.ComponentModel;
using EGG9000.Bot.Automated.Coops;
using Microsoft.AspNetCore.Connections.Features;

namespace EGG9000.Bot.Commands {
    public static class MiscCommandsSlash {
        [SlashCommand(Description = "Track your EB since the last time you ran this command", AllowInDMs = true)]
        public static async Task TrackEB(FauxCommand command, ApplicationDbContext db, ILogger logger) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var builder = new EmbedBuilder {
                Title = $"EB Tracking"
            };
            foreach(var id in user.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                if(user.EggIncAccounts.Count > 1) {
                    builder.AddField("――――――――――――――――――", $"**{backup.UserName}**");
                }

                var backupDate = DateTimeOffset.FromUnixTimeSeconds(backup.LastBackupTime);

                if(id.LastEBTime.HasValue) {
                    builder.AddField("Last EB", $"{id.LastEB.ToEggString()}\n{DiscordHelpers.TimeStamper(id.LastEBTime.Value, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);
                }

                builder.AddField("Current EB", $"{backup.EarningsBonus.ToEggString()}\n{DiscordHelpers.TimeStamper(backupDate, DiscordHelpers.DiscordTimestampFormat.Relative)}", true);

                if(id.LastEBTime.HasValue) {
                    var change = backup.EarningsBonus - id.LastEB;
                    var percentChange = change / id.LastEB * 100d;

                    var format = percentChange == (int)percentChange ? "F0" : "F2";

                    var timeStampDifference = (id.LastEBTime.Value - backupDate).Humanize();
                    builder.AddField("EB Gained", $"{change.ToEggString()} (+{percentChange.ToString(format)}%)\n{timeStampDifference}", true);
                } else {
                    builder.AddField("First Update", "No previous EB to compare to", true);
                }

                id.LastEB = backup.EarningsBonus;
                id.LastEBTime = backupDate;
            }

            user.UpdateAccounts();
            await db.SaveChangesAsync();
            await command.ModifyOriginalResponseAsync(x => {
                x.Embed = builder.Build();
                x.Content = "";
            });
        }

        [SlashCommand(Description = "How many SE/PE needed for next rank up", AllowInDMs = true)]
        public static async Task NextRank(FauxCommand command, ApplicationDbContext db, [SlashParam(Required = false)] bool ShowInChannel = false) {
            await command.RespondAsync("Getting backups...", ephemeral: !ShowInChannel);
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var builder = new EmbedBuilder();
            builder.Title = $"Next Rank Details";
            foreach(var id in user.EggIncAccounts) {
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                var nextSubRank = SIPrefix.GetNextRankInfo(backup, true);

                var nextRankText = "";
                foreach(var subrank in nextSubRank.Take(5)) {
                    nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                    if(subrank.SoulsEggs < 0)
                        break;
                }

                builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextSubRank.First().Rank} [{nextSubRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });

                var nextRank = SIPrefix.GetNextRankInfo(backup, false);
                var currentRank = SIPrefix.GetPrefixFromEB(backup.EarningsBonus);
                if(nextRank.First().SoulsEggs != nextSubRank.First().SoulsEggs) {
                    nextRankText = "";
                    foreach(var subrank in nextRank.Take(5)) {
                        nextRankText += $"<:Egg_of_Prophecy_PE:669981330477547580>{subrank.EggsOfProphecy} <:Soul_Egg_SE:724341890794913964>{Math.Max(0, subrank.SoulsEggs).ToEggString()}\n";
                        if(subrank.SoulsEggs < 0)
                            break;
                    }

                    builder.AddField(new EmbedFieldBuilder { IsInline = true, Name = (user.EggIncAccounts.Count > 1 ? $"{backup.UserName}\n" : "") + $"{nextRank.First().Rank} [{nextRank.First().EarningsBonus.ToEggString()}]", Value = nextRankText });
                }

                var ge = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
                builder.AddField(new EmbedFieldBuilder {
                    IsInline = false, Name = "Current Details", Value = @$"{currentRank.RankWithSubRank}
<:Egg_of_Prophecy_PE:669981330477547580>{backup.EggsOfProphecy}
<:Soul_Egg_SE:724341890794913964>{backup.SoulEggs.ToEggString(numberOfDecimalPlaces: 3)}
EB {backup.EarningsBonus.ToEggString(numberOfDecimalPlaces: 3)}
Prestiges {backup.NumPrestiges}
<:Soul_Egg_SE:724341890794913964>/Prestige {(backup.SoulEggs / backup.NumPrestiges).ToEggString(numberOfDecimalPlaces: 3)}
<:Golden_Egg_GE:692439755798872075> {(ge > 1_000_000_000 ? ge.ToEggString(numberOfDecimalPlaces: 3) : ge.ToString("n0"))}
<:Piggy_bank:724396277676113955>  {(backup.TotalGEInPiggyBank > 1_000_000_000 ? backup.TotalGEInPiggyBank.ToEggString(numberOfDecimalPlaces: 3) : backup.TotalGEInPiggyBank.ToString("n0"))}
<:Drone:755719353529270342> {backup.DroneTakedowns.ToString("n0")}
<:Drone:755719353529270342> Elite {backup.DroneTakedownsElite.ToString("n0")}
Last Backup <t:{backup.LastBackupTime}:R>
"
                });
            }

            //await command.Channel.SendMessageAsync($"{command.User.Mention} used the command `/nextrank`", embed: builder.Build());
            //await command.DeleteResponseFix();
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = builder.Build();
            });
        }

        [SlashCommand(Description = "Rename a co-op channel to mistype", AdminOnly = StaffOnlyLevel.FarmHand)]
        public static async Task RenameCoop(FauxCommand command, ApplicationDbContext db, [SlashParam] string correctcoopname) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop == null) {
                await command.RespondAsync($"⚠️ERROR: Command only works in co-op channels");
                return;
            }


            targetCoop.Name = correctcoopname;
            await db.SaveChangesAsync();
            await command.RespondAsync($"Co-op renamed to {correctcoopname}");
        }

        [SlashCommand(Description = "Trigger an update for a co-op or contract channel", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task UpdateChannel(FauxCommand command, ApplicationDbContext db, CoopStatusUpdater coopStatusUpdater, DiscordSocketClient discord, ContractUpdater contractUpdater, APILink apiLink) {
            var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetCoop != null) {
                await command.RespondAsync("Updating coop...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetCoop.OverflowGuildId);
                var users = await db.DBUsers.AsQueryable().Where(x => x.UserCoopXrefs.Any(y => y.CoopId == targetCoop.Id)).ToListAsync();
                var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == targetCoop.GuildId);
                await coopStatusUpdater.ProcessCoop(targetCoop.Id, guild, users.SelectMany(x => x.EggIncAccounts.Select(y => new UserWithBackup { Backup = y.Backup, User = x })).ToList(), dbguild, default, db);
                await command.ModifyOriginalResponseAsync(m => m.Content = "Co-op Updated");
                return;
            }

            var targetGuildContract = await db.GuildContracts.Include(x => x.Contract).AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
            if(targetGuildContract != null) {
                await command.RespondAsync("Updating contract...", ephemeral: true);
                var guild = discord.Guilds.First(x => x.Id == targetGuildContract.GuildID);

                //var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id && !x.TempDisabled).ToListAsync();
                //var dbguild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guild.Id);
                //var backups = await apiLink.GetUserBackups(dbusers, db);
                //var backups = dbusers.Where(x => x.Backups is not null).SelectMany(x => x.Backups.Where(y => x.EggIncAccounts.Any(eid => eid.Id == y.EggIncId)).Select(y => new LeaderboardUser { User = x, Backup = y })).ToList();

                await contractUpdater.UpdateContractChannel(db, targetGuildContract, guild, command);
                await command.ModifyOriginalResponseAsync(x => x.Content = "Content Updated");
                //await command.DeleteOriginalResponseAsync();
                return;
            }

            await command.RespondAsync($"⚠️ERROR: Command only works in contract or co-op channels");
        }

        [SlashCommand(Description = "Adds a temporary role for users that last a specific amount of time", AdminOnly = StaffOnlyLevel.CluckingCoordinator)]
        public static async Task TempRole(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] SocketRole role, [SlashParam] string timespan, [SlashParam] string reason, [SlashParam] SocketGuildUser[] users) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            await command.DeferAsync();
            var userids = users.Select(x => x.Id);
            var existingTempRoles = await db.TemporaryRoles.Where(x => x.RoleId == role.Id && x.Expires > DateTimeOffset.Now && userids.Contains(x.UserId)).ToListAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            foreach(var user in users) {
                var tempRole = existingTempRoles.FirstOrDefault(x => x.RoleId == role.Id && user.Id == x.UserId);
                if(tempRole == null) {
                    tempRole = new TemporaryRole { RoleId = role.Id, Created = DateTimeOffset.Now, UserId = user.Id, GuildId = guild.Id };
                    db.Add(tempRole);
                    await user.AddRoleAsync(role);
                }

                tempRole.Reason = reason;
                tempRole.Expires = expireTime;
            }

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the role {role.Emoji} {role.Name} to the following {"user".ToQuantity(users.Count(), ShowQuantityAs.None)} {string.Join(", ", users.Select(x => x.Mention))} until <t:{expireTime.ToUnixTimeSeconds()}:f> for the reason: {reason}");
        }

        [SlashCommand(Description = "Adds a temporary name to be used for co-op naming", AdminOnly = StaffOnlyLevel.CluckingCoordinator, ParentCommand = "a")]
        public static async Task TempCustomCoopName(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string customName, [SlashParam] string timespan, [SlashParam] SocketGuildUser user) {
            DateTimeOffset expireTime;
            try {
                expireTime = timespan.AddTimeSpanString(DateTimeOffset.Now);
            } catch(Exception ex) {
                await command.RespondAsync($"Unable to parse the timespan `{timespan}`, {ex.Message}");
                return;
            }

            await command.DeferAsync();
            var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == command.Channel.Id));
            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == user.Id);
            dbuser.CustomCoopName = customName;
            dbuser.ExpireCustomCoopName = expireTime;

            await db.SaveChangesAsync();

            await command.ModifyOriginalResponseAsync(m => m.Content = $"Added the custom co-op prefix {customName} to {user.Mention} until <t:{expireTime.ToUnixTimeSeconds()}:f>");
        }

        private class AfxSetBuilder {
            public ComponentBuilder ComponentBuilder { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }
            public AfxSetBuilder() { }
        }

        public static Color RandomColor() {
            var random = new Random();

            // Generate random values for red, green, and blue components.
            var red = (byte)random.Next(256);
            var green = (byte)random.Next(256);
            var blue = (byte)random.Next(256);

            // Create and return the Discord.Color.
            return new Color(red, green, blue);
        }

        public static string GetAfxSetString(List<EggIncArtifactInstance> set) {
            return string.Join("\n", set.Select(s => ArtifactHelpers.GetAfEmoji(s) + ArtifactHelpers.GetRarityEmoji(s) + string.Join("", s.Stones.Select(st => ArtifactHelpers.GetAfEmoji(st)).ToList())));
        }

        public static string GetAfxString(EggIncArtifactInstance instance) {
            return ArtifactHelpers.GetAfEmoji(instance) + ArtifactHelpers.GetRarityEmoji(instance) + string.Join("", instance.Stones.Select(st => ArtifactHelpers.GetAfEmoji(st))).ToString();
        }

        [SlashCommand(Description = "Show off your saved Artifact Sets")]
        public static async Task SavedAfSets(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }
            var accountIndex = int.Parse(useraccount.Split("|")[1]);
            var account = user.EggIncAccounts[accountIndex];
            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null || afxSets.Count == 0) {
                await command.ModifyOriginalResponseAsync("⚠️ERROR: Backup is empty, or no Artifact Sets were found for this account");
                return;
            }

            var builder = AFXSetEmbedBuilder(user, accountIndex, afxSets, afxSets[0]);
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Components = builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        [ComponentCommand]
        public static async Task LoadAFXSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {

            var dataItems = data.Split(",");
            var discordId = ulong.Parse(dataItems[0] ?? "-1");
            var accountIndex = int.Parse(dataItems[1] ?? "-1");
            var currentSetIndex = int.Parse(dataItems[2] ?? "-1");

            if(discordId < 0 || accountIndex < 0 || currentSetIndex < 0) return;

            var user = db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
            if(user is null || user.EggIncAccounts.Count -1 < accountIndex) return;

            var account = user.EggIncAccounts[accountIndex];
            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null) return;

            var builder = AFXSetEmbedBuilder(user, accountIndex, afxSets, afxSets[currentSetIndex]);
            await component.UpdateAsync(x => {
                x.Content = "";
                x.Components = builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        private static AfxSetBuilder AFXSetEmbedBuilder(DBUser user, int accountIndex, List<List<EggIncArtifactInstance>> afxSets, List<EggIncArtifactInstance> currentSet) {
            var builder = new AfxSetBuilder() {
                ComponentBuilder = null
            };

            var componentBuilder = new ComponentBuilder();
            var buttonCount = 0;

            var currentSetIndex = afxSets.IndexOf(currentSet);
            var setsCount = afxSets.Count;

            var embedBuilder = new EmbedBuilder().WithAuthor(
                new EmbedAuthorBuilder()
                    .WithName($"Set {currentSetIndex + 1}")
                    .WithIconUrl("https://cdn.discordapp.com/emojis/877681508607987772.webp")
                ).WithColor(RandomColor())
                .WithDescription(GetAfxSetString(currentSet));

            if(currentSetIndex > 0 && setsCount > 1 && afxSets[currentSetIndex - 1] is not null) {
                componentBuilder.WithButton($"← Set {currentSetIndex}", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex - 1}"); buttonCount++;
            }
            if(currentSetIndex < afxSets.Count - 1 && afxSets[currentSetIndex + 1] is not null) {
                componentBuilder.WithButton($"Set {currentSetIndex + 2} →", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex + 1}"); buttonCount++;
            }
            if(buttonCount > 0) builder.ComponentBuilder = componentBuilder;

            builder.EmbedBuilder = embedBuilder;
            return builder;
        }

        public class PersonalUserAccountAutoComplete : AutoCompleteHandler {
            private readonly ApplicationDbContext _db;
            public PersonalUserAccountAutoComplete(ApplicationDbContext db) {
                _db = db;
            }
            public async Task Run(SocketAutocompleteInteraction arg) {
                var guild = await _db.Guilds.FirstAsync(x => x.Id == arg.GuildId || x.OverflowServersJson.Contains(arg.GuildId.ToString()));
                var users = await _db.DBUsers
                    .Where(x => x.GuildId == guild.Id && x.DiscordId == arg.User.Id)
                    .Take(10).ToListAsync();

                var accounts = users.SelectMany(x => x.EggIncAccounts.Select(y => new { User = x, Account = y })).OrderBy(x => x.Account.Backup?.EarningsBonus);

                var results = new List<AutocompleteResult>();
                foreach(var account in accounts.DistinctBy(x => x.Account.Id)) {
                    if(account.User.EggIncAccounts.Count > 1) {
                        var name = account.Account.Backup?.UserName;
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Backup?.UserName ?? "(No Name)"} {account.Account.Backup.EarningsBonus.ToEggString()}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    } else {
                        results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.User.EggIncAccounts.ToList().IndexOf(account.Account)}"));
                    }
                }

                await arg.RespondAsync(null, results.ToArray());
            }
        }

        [SlashCommand(Description = "Daveed testing")]
        public static async Task DMMe(FauxCommand command, ApplicationDbContext db) {
            var dmChannel = await command.User.CreateDMChannelAsync();
            var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, $"Test DM");
            var dbUser = db.DBUsers.FirstOrDefault(u => u.DiscordId == command.User.Id);
            if(dbUser is not null && (retEx == null) == dbUser.DMSBlocked) {
                dbUser.DMSBlocked = !dbUser.DMSBlocked;
                await db.SaveChangesAsync();
            } if(retEx != null) {
                await command.RespondAsync("Exception caught.");
            } else await command.RespondAsync("Sent");
        }

        [SlashCommand(Description = "Get help from staff, please give details")]
        public static async Task CallStaff(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false)
        {
            var guildFind = db.Guilds.First(x => x.Id == command.GuildId || x.OverflowServersJson.IndexOf(command.GuildId.ToString()) > -1);

            if(guildFind is null) {
                await command.RespondAsync("Callstaff cannot be sent, guild not found.");
                return;
            } else if(!guildFind.HasChannel(GuildChannelType.CallStaffChannel)) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel is not set.");
                return;
            }

            var socketGuild = client.Guilds.First(x => x.Id == guildFind.Id);

            if(socketGuild is null) {
                await command.RespondAsync("Callstaff cannot be sent, SocketGuild could not be found via mapping.");
                return;
            }

            var channel = socketGuild.TextChannels.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffChannel).Id);

            if(channel is null) {
                await command.RespondAsync("Callstaff cannot be sent, CallStaffChannel could not be found.");
                return;
            }

            var staffRole = socketGuild.Roles.FirstOrDefault(x => x.Id == guildFind.ChannelDetails.FirstOrDefault(c => c.ChannelType == GuildChannelType.CallStaffTagRole).Id);
            var staffTag = staffRole is null ? "" : $"<@&{staffRole.Id}>: ";

            var infoText = $"Staff has been called ({details})";

            await channel.SendMessageAsync($"{staffTag}{command.User.Mention}{(keepPrivate ? " **privately** " : " ")}called for staff in <#{command.Channel.Id}> with the details: {details}");
            await command.RespondAsync(infoText, ephemeral: keepPrivate);
            if(keepPrivate) {
                var dmChannel = await command.User.CreateDMChannelAsync();
                var retEx = await DiscordHelpersExt.BoolSendDm(dmChannel, infoText);
                var dbUser = db.DBUsers.FirstOrDefault(u => u.DiscordId == command.User.Id);
                if(dbUser is not null && (retEx == null) == dbUser.DMSBlocked) {
                    dbUser.DMSBlocked = !dbUser.DMSBlocked;
                    await db.SaveChangesAsync();
                }
                if(retEx != null) await command.Channel.SendMessageAsync($"Private callstaff sent. (DMs are blocked)");
            }
        }
    }
}