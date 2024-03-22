using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using Humanizer;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Helpers;
using Ei;
using EGG9000.Common.Migrations;
using Polly;
using Microsoft.Data.SqlClient;
using EGG9000.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using EGG9000.Common.Contracts;
using Microsoft.Extensions.Logging;
using EGG9000.Common.Factories;

namespace EGG9000.Bot.Automated {
    public class ContractUpdater : _UpdaterBase<ContractUpdater> {
        public static TimeSpan _updateInterval = TimeSpan.FromMinutes(90);
        private APILink _apiLink;
        public static List<UserX> _users;
        private Words _words;

        private Polly.Retry.AsyncRetryPolicy sqlExcpetionPolicy = Policy.Handle<SqlException>().WaitAndRetryAsync(new[]{
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(3)
        });

        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public ContractUpdater(
            APILink apilink,
            Words words,
            IServiceProvider provider
        ) : base(_updateInterval, TimeSpan.Zero, provider) {
            _users = new List<UserX>();
            _apiLink = apilink;
            _words = words;
        }


        public override async Task Run(object state, CancellationToken cancellationToken) {
            var times = new TimingsFactory(_logger);
            times.Start();

            var _db = await _dbContextFactory.CreateDbContextAsync();
            var guildContracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => !x.DeletedChannel).ToListAsync();
            times.Set("guildcontracts");

            var dbGuilds = await _db.Guilds.AsQueryable().ToListAsync();
            times.Set("dbguilds");
            var coops = await _db.Coops.Where(x => x.Created > DateTimeOffset.Now.AddDays(-14)).Select(x => new { x.Name }).ToListAsync();
            times.Set("coops");
            var guildGroups = guildContracts.GroupBy(x => x.GuildID);

            var timings = times.Finished();

#if DEBUG
            //guildGroups = guildGroups.Where(x => x.Key == dbguilds.First(x => x.Name.Contains("ingham")).DiscordSeverId);
#endif

            foreach(var dbguild in dbGuilds) {
                if(cancellationToken.IsCancellationRequested)
                    break;
                //foreach(var groupGuildContracts in guildGroups.OrderBy(x => new Guid())) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild == null)
                    continue;

                _logger.LogInformation("Running Contracts for {guild}", guild.Name);
                var dbusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync();


#if DEBUG
                //_ = await _apiLink.GetUserBackups(dbusers, _db, forceAll: true);
                //dbusers = dbusers.Take(100).ToList();
                _ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                //await ShipReturnDM.UpdateNextShipDM(dbusers, _db, _logger);
#else
                _ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                await ShipReturnDM.UpdateNextShipDM(dbusers, _db, _logger);
#endif

                var groupGuildContracts = guildGroups.FirstOrDefault(x => x.Key == dbguild.DiscordSeverId);
                //var contractIds = groupGuildContracts.Select(x => x.ContractID);




                if(groupGuildContracts is not null) {
                    foreach(var guildContract in groupGuildContracts.OrderByDescending(x => x.Created)) {
                        if(cancellationToken.IsCancellationRequested)
                            break;
                        await UpdateContractChannel(_db, guildContract, guild, dbguild);
                    }
                }


                var contracts = await _db.Contracts.ToListAsync();
                var count = 0;
                var potentialCoops = new List<(string contractid, string coopname, List<Guid> userids, ulong guildid, uint grade, long endtime)>();
                foreach(var user in dbusers) {
                    foreach(var account in user.EggIncAccounts.Where(x => x.Backup?.Farms is not null)) {
                        if(account.Backup is null)
                            continue;


                        var farms = account.Backup.Farms.Where(x =>
                            x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset &&
                            !x.Completed &&
                            !coops.Any(c => c.Name.Equals(x.CoopId, StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrWhiteSpace(x.CoopId) && x.TimeAccepted > DateTimeOffset.Now.AddDays(-7).ToUnixTimeSeconds()
                        );

                        foreach(var farm in farms) {

                            if(potentialCoops.Any(y => y.contractid == farm.ContractId && y.coopname == farm.CoopId)) {
                                var poentialCoop = potentialCoops.First(y => y.contractid == farm.ContractId && y.coopname == farm.CoopId);
                                poentialCoop.userids.Add(user.Id);
                                poentialCoop.userids = poentialCoop.userids.Distinct().ToList();
                            } else {
                                potentialCoops.Add((farm.ContractId, farm.CoopId, new List<Guid> { user.Id }, user.GuildId, (uint)farm.Grade, farm.CoopSharedEndTime));
                            }
                        }
                    }
                }

                foreach(var pCoop in potentialCoops.Where(x => x.userids.Count > 1)) {

                    var dbGuild = await _db.Guilds.FirstOrDefaultAsync(g => g.Id == pCoop.guildid);
                    if(!dbGuild.AddOutsideCoops) continue;

                    var contract = contracts.First(x => x.ID == pCoop.contractid);
                    var exisitingCoop = await _db.Coops.FirstOrDefaultAsync(x => x.GuildId == pCoop.guildid && x.ContractID == pCoop.contractid && EF.Functions.Like(x.Name, pCoop.coopname));
                    if(exisitingCoop is not null) {
                        _logger.LogInformation("Co-op {coopname} already exists, skipping", pCoop.coopname);
                        continue;
                    }
                    _logger.LogInformation("Adding co-op {coopname} from backups", pCoop.coopname);
                    var coop = new Coop {
                        ContractID = pCoop.contractid, Created = DateTimeOffset.Now, GuildId = pCoop.guildid, Name = pCoop.coopname,
                        MaxUsers = contract.MaxUsers, Status = CoopStatusEnum.WaitingOnAssigned, League = pCoop.grade,
                        CoopEnds = DateTimeOffset.FromUnixTimeSeconds(pCoop.endtime),
                        AddedFromBackup = true,
                    };
                    coops.Add(new { Name = coop.Name });
                    _db.Add(coop);
                    await _db.SaveChangesAsync();
                    count++;
                }

            }

            await _db.SaveChangesAsync();
        }

        public static Embed GetContractEmbed(GuildContract guildContract, SocketGuild guild, Ei.Contract.Types.PlayerGrade grade = Ei.Contract.Types.PlayerGrade.GradeUnset) {
            var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);
            var description = $"**Size** {guildContract.Contract.Details.MaxCoopSize}, **<:Token_boost:724397091211968604>** {guildContract.Contract.Details.MinutesPerToken}mins,";
            description += $"**{(validFor > TimeSpan.Zero ? "  Expires " : " Expired ")}** {DiscordHelpers.TimeStamper(validFor)}";
            if(guildContract.BoardingGroup < 3)
                description += $"\n[View Upcoming Co-ops on egg9000.com](https://egg9000.com/Contract/Day1CoopsFillLate?GuildId={guild.Id}&ContractId={guildContract.ContractID})";

            var embedBuilder = new EmbedBuilder()
                .WithDescription(description)
                .WithAuthor(
                    new EmbedAuthorBuilder().WithName($"{guildContract.Contract.Name} - {guildContract.Contract.ID}")
                    .WithIconUrl(EggIncEggs.GetEggById((int)guildContract.Contract.Details.Egg).Image));

            var startIndex = grade == Ei.Contract.Types.PlayerGrade.GradeUnset ? 5 : (int)grade;
            var endIndex = grade == Ei.Contract.Types.PlayerGrade.GradeUnset ? 1 : (int)grade;

            for(var i = startIndex; i >= endIndex; i--) {
                var gradeSpec = guildContract.Contract.Details.GradeSpecs[i - 1];
                var maxTargetLength = gradeSpec.Goals.Select(x => x.TargetAmount.ToEggString()).Max(x => x.Length);
                var goals = string.Join("\n", gradeSpec.Goals.Select(x => $"`{x.TargetAmount.ToEggString().PadLeft(maxTargetLength)}` {EggIncEggs.GetReward(x)}"));
                var length = TimeSpan.FromSeconds(gradeSpec.LengthSeconds);
                goals += $"\n**Length**: {length.Humanize(precision: 2).ShortenTime()}";
                if(gradeSpec.Modifiers.Any()) {
                    goals += "\n**Modifiers:**\n" + string.Join("\n", gradeSpec.Modifiers.Select(x => $"{x.Dimension} {(x.Value < 1 ? $"{x.Value * 100}%" : $"{x.Value}x")}"));
                }
                embedBuilder.AddField(PlayerGradeDetails.GetEmoji((Ei.Contract.Types.PlayerGrade)i), goals, inline: true);
            }

            return embedBuilder.Build();
        }

        public async Task UpdateContractChannel(ApplicationDbContext _db, GuildContract guildContract, SocketGuild guild,  Guild dbGuild, FauxCommand slashCommand = null) {
            try {
                _logger.LogInformation("Working on GuildContract for {guild} - {contract}", guild.Name, guildContract.Contract.Name);

                var channel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);


                if(guildContract.Contract.GoodUntil.AddSeconds(guildContract.Contract.Details.LengthSeconds).AddDays(1) < DateTimeOffset.Now) {
                    if(channel != null) {
                        await channel.DeleteAsync();
                    }
                    await dbGuild.DeleteCoopThreadHeaders(_client, guildContract.Contract);
                    guildContract.DeletedChannel = true;
                    await _db.SaveChangesAsync();
                    return;
                }


                if(channel == null) {
                    _logger.LogWarning("Missing Channel for {contract} in {guild}", guildContract.Contract.Name, guild.Name);
                    return;
                }

                await UpdateContractChannelName(guildContract, channel, guild);

                var condensedMsgs = new List<string>();
                var newMsgs = new List<string>();
                var currentMsg = "";
                for(var i = 0; i < newMsgs.Count; i++) {
                    var msg = newMsgs[i];
                    var cmsg = msg;
                    if(currentMsg.Length + cmsg.Length > 2000) {
                        condensedMsgs.Add(currentMsg);
                        currentMsg = "";
                        while(cmsg.Length > 2000) {
                            var index = Math.Max(cmsg.LastIndexOf('\n', 2000), cmsg.LastIndexOf(',', 2000));
                            condensedMsgs.Add(cmsg[..index]);
                            cmsg = cmsg[index..];
                        }
                    }
                    currentMsg += cmsg;
                }
                condensedMsgs.Add(currentMsg);


                var rawTextMessageAspect = guildContract.CcOnly ? "__**<:ultra:1131045418319495369> Subscriber-Only Contract:**__\r\n\r\nThis contract will only appear in-game for Egg, Inc. ULTRA Standard, and ULTRA Pro players." : "";

                var existingMessages = (await channel.GetMessagesAsync(limit: 1000).FlattenAsync()).ToList();

                var nonBotMessages = existingMessages.Where(x => !x.Author.IsBot || x.Interaction?.Type == InteractionType.ApplicationCommand).ToList();
                if(nonBotMessages.Count > 0) {
                    await channel.DeleteMessagesBatchAsync(nonBotMessages);
                }

#if DEV9002
                var findSpotButton = new ComponentBuilder().WithButton("Find Coop Spot", customId: $"FindCoopSpot").Build();
#else
                var bgsLaunched = dbGuild.DisableBG || (DateTimeOffset.Now > guildContract.Contract.Created.AddHours(guildContract.CcOnly ? 24 : 18));
                var findSpotButton = (bgsLaunched && guildContract.Contract.GoodUntil > DateTimeOffset.Now) ? new ComponentBuilder().WithButton("Find Coop Spot", customId: $"FindCoopSpot").Build() : null;
#endif

                existingMessages = existingMessages.Where(x => x.Author.IsBot).OrderBy(x => x.CreatedAt).ToList();

                var contractEmbed = GetContractEmbed(guildContract, guild);

                if(existingMessages.Count > 0) {
                    await (existingMessages.First() as RestUserMessage).ModifyWithTimeoutAsync(msg => {
                        msg.Embed = contractEmbed;
                        msg.Content = rawTextMessageAspect;
                        msg.Components = (dbGuild.RemoveFindCoopSpot ? null : findSpotButton);
                    });
                } else {
                    await channel.SendMessageAsync(rawTextMessageAspect, embed: contractEmbed, components: dbGuild.RemoveFindCoopSpot ? null : findSpotButton);
                }


            } catch(Exception e) {
                _logger.LogError(e, "Error Updating Contracts Channel");
                _bugsnag.Notify(e);
            }

        }

        public static string GetLinkToMessage(IMessage message, IGuild guild, ITextChannel channel, string text) {
            if(message == null)
                return "";
            return $"[{text}](https://discord.com/channels/{guild.Id}/{channel.Id}/{message.Id})\n";
        }

        public static string Truncate(string value, int maxLength) {
            if(string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        public async Task UpdateContractChannelName(GuildContract guildContract, SocketTextChannel channel, SocketGuild guild) {
            var channelName = guildContract.Contract.Name.Split(":").Last().Trim().Replace(" ", "-");
            var validFor = DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now;
            var emoji = "";

            if(guildContract.CcOnly) {
                var subCategory = await _client.GetCategoryAsync(GuildChannelType.SubscriptionContractCategory, guild);
            }
            emoji += DateTimeOffset.Now >= DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) ? "⛔" : ( guildContract.CcOnly ? "💰" : "✅");     

            channelName = emoji + channelName;

            if(channelName != channel.Name) {
                try {
                    await channel.ModifyAsync(x => x.Name = channelName);
                } catch(Exception) {

                }
            }
        }

    }
}
