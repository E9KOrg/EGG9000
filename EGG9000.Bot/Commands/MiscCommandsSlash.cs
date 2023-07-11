using Discord;
using Discord.Interactions;
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

namespace EGG9000.Bot.Commands {
    public static class MiscCommandsSlash {
        [Common.Commands.SlashCommand(Description = "Show you required artifacts to craft the requested aritfact.")]
        public static async Task CraftArtifact(FauxCommand command, [SlashParam(Description = "Quantity"), MinValue(1)] int quantity, [SlashParam(Description = "Tier"), MinValue(2), MaxValue(4)] int quality, [SlashParam(Description = "artifact")] string artifact, ApplicationDbContext db, ILogger logger) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var stringBuilder = new StringBuilder();
            for(var i = 0; i < 3; i++) {
                var id = user.EggIncAccounts[0];
                var backup = id.Backup;
                if(backup == null)
                    continue;
                backup = new CustomBackup((await ContractsAPI.FirstContact(id.Id)).Backup);
                if(i == 0) {
                    stringBuilder.Append($"For **{backup.UserName}** to craft {quantity} T{quality} {artifact}:");
                    stringBuilder.AppendLine();
                } else {
                    stringBuilder.AppendLine();
                    stringBuilder.Append("―――――――――――――――――――――――――――――――――――――――");
                    stringBuilder.AppendLine();
                    stringBuilder.Append($"For **{backup.UserName}** to craft {quantity} T{quality} {artifact}:");
                    stringBuilder.AppendLine();
                }

                var crafter = new Crafter(backup.ArtifactHall);
                var basket = crafter.GetCraft(quantity, quality, artifact);

                stringBuilder.AppendFormat($"```{"Name",-15}{"Using",-8}{"Need",-8}{"Cost",-8}");
                stringBuilder.AppendLine();
                stringBuilder.Append("―――――――――――――――――――――――――――――――――――――――");
                stringBuilder.AppendLine();

                var ingredients = from kvp in basket.GetIngredients()
                    orderby EggIncArtifacts.GetFamilyShorthand(kvp.Value.Tier.family) ascending, kvp.Value.Tier.tier_number descending
                    select kvp;
                foreach(var ingredient in ingredients) {
                    stringBuilder.AppendFormat($"{$"T{ingredient.Value.Tier.tier_number} {EggIncArtifacts.GetFamilyShorthand(ingredient.Value.Tier.family)}",-15}");
                    stringBuilder.AppendFormat($"{ingredient.Value.Use,-8}");
                    stringBuilder.AppendFormat($"{ingredient.Value.GetNeed(),-8}");
                    stringBuilder.AppendFormat($"{ingredient.Value.Cost.Format(),-8}");
                    stringBuilder.AppendLine();
                }

                stringBuilder.AppendLine("```");
                stringBuilder.Append($"Total Cost: **{basket.GetTotalCost().ToString("#,0", new CultureInfo("en-US"))} GE**");
                stringBuilder.AppendLine();
                var goldenEggs = backup.GoldenEggsEarned - backup.GoldenEggsSpent;
                stringBuilder.Append(goldenEggs >= basket.GetTotalCost() ? "You have enough GE!" : "You do not have enough GE!");
            }

            user.UpdateAccounts();
            await db.SaveChangesAsync();


            await command.ModifyOriginalResponseAsync(x => {
                x.Embed = null;
                x.Content = $"\n{stringBuilder.ToString()}\n";
            });
            //await command.ModifyOriginalResponseAsync(x => { x.Content = $"```\n{stringBuilder.ToString()}\n```"; });
        }

        [Common.Commands.SlashCommand(Description = "Track your EB since the last time you ran this command")]
        public static async Task TrackEB(FauxCommand command, ApplicationDbContext db, ILogger logger) {
            await command.RespondAsync("Getting backups...");
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            var builder = new EmbedBuilder();
            builder.Title = $"EB Tracking";
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
                    if(percentChange >= 10)
                        percentChange = Math.Round(percentChange);
                    var digits = (int)Math.Log10(percentChange) - 2;
                    var format = $"F{(digits < 1 ? -digits + 2 : 0)}";
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

        [Common.Commands.SlashCommand(Description = "How many SE/PE needed for next rank up")]
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

        [Common.Commands.SlashCommand(Description = "Rename a co-op channel to mistype", AdminOnly = true)]
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

        //[SlashCommand(Description = "Get a ping from the bot via DM and all assigned members have joined")]
        //public static async Task PingOnFull(FauxCommand command, ApplicationDbContext db)
        //{
        //    var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
        //    if(targetCoop == null)
        //    {
        //        await command.RespondAsync($"⚠️ERROR: Command only works in co-op channels", ephemeral: true);
        //        return;
        //    }
        //    var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == command.User.Id);

        //    var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == user.Id && x.Coop.DiscordChannelId == command.Channel.Id);

        //    xref.PingOnFull = !xref.PingOnFull;
        //    await db.SaveChangesAsync();
        //    if(xref.PingOnFull)
        //    {
        //        await command.RespondAsync($"Will receive DM ping when everyone has joined", ephemeral: true);
        //    } else
        //    {
        //        await command.RespondAsync($"Will no longer receive ping", ephemeral: true);
        //    }
        //}

        //[SlashCommand(Description = "Get a ping from the bot via DM on Highest EB Joined")]
        //public static async Task PingOnHighestEB(FauxCommand command, ApplicationDbContext db) {
        //    var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
        //    if(targetCoop == null) {
        //        await command.RespondAsync($"⚠️ERROR: Command only works in co-op channels", ephemeral: true);
        //        return;
        //    }
        //    var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == command.User.Id);

        //    var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == user.Id && x.Coop.DiscordChannelId == command.Channel.Id);

        //    xref.PingOnHighestEB = !xref.PingOnHighestEB;
        //    await db.SaveChangesAsync();
        //    if(xref.PingOnHighestEB) {
        //        await command.RespondAsync($"Will receive DM ping when the highest EB has joined", ephemeral: true);
        //    } else {
        //        await command.RespondAsync($"Will no longer receive a ping when the highest EB has joined", ephemeral: true);
        //    }
        //}
        //[SlashCommand(Description = "Get a ping from the bot via DM when co-op is finished")]
        //public static async Task PingOnFinished(FauxCommand command, ApplicationDbContext db) {
        //    var targetCoop = await db.Coops.AsQueryable().FirstOrDefaultAsync(x => x.DiscordChannelId == command.Channel.Id);
        //    if(targetCoop == null) {
        //        await command.RespondAsync($"⚠️ERROR: Command only works in co-op channels", ephemeral: true);
        //        return;
        //    }
        //    var user = await db.DBUsers.AsQueryable().FirstAsync(x => x.DiscordId == command.User.Id);

        //    var xref = await db.UserCoopXrefs.AsQueryable().FirstAsync(x => x.UserId == user.Id && x.Coop.DiscordChannelId == command.Channel.Id);

        //    xref.PingOnFinished = true;
        //    await db.SaveChangesAsync();
        //    await command.RespondAsync($"Will receive DM ping when co-op is finished and everyone has reported in", ephemeral: true);
        //}

        [Common.Commands.SlashCommand(Description = "Trigger an update for a co-op or contract channel", AdminOnly = true)]
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

        [Common.Commands.SlashCommand(Description = "Adds a temporary role for users that last a specific amount of time", AdminOnly = true, AllowFarmHand = true)]
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

        [Common.Commands.SlashCommand(Description = "Adds a temporary name to be used for co-op naming", AdminOnly = true, ParentCommand = "a", CPOnly = true)]
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

        [Common.Commands.SlashCommand(Description = "Get help from staff, please give details", CPOnly = true)]
        public static async Task CallStaff(FauxCommand command, ApplicationDbContext db, DiscordSocketClient client, [SlashParam] string details, [SlashParam(Description = "If private then only staff will see your message", Required = false)] bool keepPrivate = false) {
            var channel = client.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 940777970111488050);
            await channel.SendMessageAsync($"<@&904799345122091018>: {command.User.Mention} called for staff in <#{command.Channel.Id}> with the details: {details}");
            if(keepPrivate) {
                await command.RespondAsync("Staff has been called.", ephemeral: true);
            } else {
                await command.RespondAsync($"Staff has been called ({details})");
            }
        }
    }
}