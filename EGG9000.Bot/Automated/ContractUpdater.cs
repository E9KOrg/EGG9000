using Discord;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using EGG9000.Common.Services;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Bot.Automated {
    public class ContractUpdater(IServiceProvider provider) : _UpdaterBase<ContractUpdater>(_updateInterval, TimeSpan.Zero, provider) {
        public static readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(90);
        public readonly List<UserX> _users = [];

        public class UserX {
            public SocketGuildUser SocketGuildUser { get; set; }
            public Guid DBUserId { get; set; }
        }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var times = new TimingsFactory(_logger);
            times.Start();

            var _db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var guildContracts = await _db.GuildContracts.Include(x => x.Contract).Where(x => !x.DeletedChannel).ToListAsync(CancellationToken.None);
            times.Set("guildcontracts");

            var dbGuilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);
            times.Set("dbguilds");
            var coops = await _db.Coops.Where(x => x.Created > DateTimeOffset.Now.AddDays(-14)).Select(x => new { x.Name }).ToListAsync(CancellationToken.None);
            times.Set("coops");
            var guildGroups = guildContracts.GroupBy(x => x.GuildID);

            var timings = times.Finished();

#if DEBUG
            //guildGroups = guildGroups.Where(x => x.Key == dbguilds.First(x => x.Name.Contains("ingham")).DiscordSeverId);
            //dbGuilds = dbGuilds.Where(x => x.Id == 1113544827750076567).ToList();
#endif

            foreach(var dbguild in dbGuilds) {
                if(cancellationToken.IsCancellationRequested)
                    break;
                //foreach(var groupGuildContracts in guildGroups.OrderBy(x => new Guid())) {
                var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.DiscordSeverId);
                if(guild == null)
                    continue;

                _logger.LogInformation("Running Contracts for {guild}", guild.Name);
                var dbusers = await _db.DBUsers.AsQueryable().Where(x => x.GuildId == guild.Id).ToListAsync(CancellationToken.None);


#if DEBUG
                //_ = await _apiLink.GetUserBackups(dbusers, _db, forceAll: true);
                //dbusers = dbusers.Take(100).ToList();
                //_ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                //await ShipReturnDM.UpdateNextShipDM(dbusers, _db);
#else
                _ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                await ShipReturnDM.UpdateNextShipDM(dbusers, _db);
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


                var contracts = await _db.Contracts.ToListAsync(CancellationToken.None);
                var count = 0;
                var potentialCoops = new List<(string contractid, string coopname, List<Guid> userids, ulong guildid, uint grade, long endtime)>();
                foreach(var user in dbusers) {
                    foreach(var account in user.EggIncAccounts.Where(x => x.Backup?.Farms is not null)) {
                        if(account.Backup is null)
                            continue;


                        var farms = account.Backup.Farms.Where(x =>
                            x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset &&
                            !coops.Any(c => c.Name.Equals(x.CoopId, StringComparison.OrdinalIgnoreCase)) &&
                            !string.IsNullOrWhiteSpace(x.CoopId) && x.TimeAccepted > DateTimeOffset.Now.AddDays(-7).ToUnixTimeSeconds()
                        );

                        foreach(var farm in farms) {

                            if(potentialCoops.Any(y => y.contractid == farm.ContractId && y.coopname == farm.CoopId)) {
                                var (contractid, coopname, userids, guildid, grade, endtime) = potentialCoops.First(y => y.contractid == farm.ContractId && y.coopname == farm.CoopId);
                                userids.Add(user.Id);
                                userids = userids.Distinct().ToList();
                            } else {
                                potentialCoops.Add((farm.ContractId, farm.CoopId, new List<Guid> { user.Id }, user.GuildId, (uint)farm.Grade, farm.CoopSharedEndTime));
                            }
                        }
                    }
                }

                foreach(var (contractid, coopname, userids, guildid, grade, endtime) in potentialCoops.Where(x => x.userids.Count > 1)) {

                    var dbGuild = await _db.Guilds.FirstOrDefaultAsync(g => g.Id == guildid, CancellationToken.None);
                    if(!dbGuild.AddOutsideCoops) continue;

                    var contract = contracts.First(x => x.ID == contractid);
                    var exisitingCoop = await _db.Coops.FirstOrDefaultAsync(x => x.GuildId == guildid && x.ContractID == contractid && EF.Functions.Like(x.Name, coopname), CancellationToken.None);
                    if(exisitingCoop is not null) {
                        _logger.LogInformation("Co-op {coopname} already exists, skipping", coopname);
                        continue;
                    }
                    _logger.LogInformation("Adding co-op {coopname} from backups", coopname);
                    var coop = new Coop {
                        ContractID = contractid, Created = DateTimeOffset.Now, GuildId = guildid, Name = coopname,
                        MaxUsers = contract.MaxUsers, Status = CoopStatusEnum.WaitingOnAssigned, League = grade,
                        CoopEnds = DateTimeOffset.FromUnixTimeSeconds(endtime),
                        AddedFromBackup = true,
                    };
                    coops.Add(new { Name = coop.Name });
                    _db.Add(coop);
                    await _db.SaveChangesAsync(CancellationToken.None);
                    count++;
                }

            }

            await _db.SaveChangesAsync(CancellationToken.None);
        }

        public static Embed GetContractEmbed(GuildContract guildContract, SocketGuild guild, Ei.Contract.Types.PlayerGrade grade = Ei.Contract.Types.PlayerGrade.GradeUnset) {
            var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);
            var description = $"**Size** {guildContract.Contract.Details.MaxCoopSize}, **<:Token_boost:724397091211968604>** {guildContract.Contract.Details.MinutesPerToken}mins,";
            description += $"**{(validFor > TimeSpan.Zero ? "  Expires " : " Expired ")}** {DiscordHelpers.TimeStamper(validFor)}";
            if(guildContract.BoardingGroup < 3)
                description += $"\n[View Upcoming Co-ops on egg9000.com](https://egg9000.com/Contract/Day1CoopsFillLate?GuildId={guild.Id}&ContractId={guildContract.ContractID})";

            var embedBuilder = new EmbedBuilder().WithDescription(description);
            var author = new EmbedAuthorBuilder().WithName($"{guildContract.Contract.Name} - {guildContract.Contract.ID}");
            
            author.WithIconUrl(EggIncStatics.GetEggByContract(guildContract.Contract).image);

            embedBuilder.WithAuthor(author);

            var startIndex = grade == Ei.Contract.Types.PlayerGrade.GradeUnset ? 5 : (int)grade;
            var endIndex = grade == Ei.Contract.Types.PlayerGrade.GradeUnset ? 1 : (int)grade;

            for(var i = startIndex; i >= endIndex; i--) {
                var gradeSpec = guildContract.Contract.Details.GradeSpecs[i - 1];
                var maxTargetLength = gradeSpec.Goals.Select(x => x.TargetAmount.ToEggString()).Max(x => x.Length);
                var goals = string.Join("\n", gradeSpec.Goals.Select(x => $"`{x.TargetAmount.ToEggString().PadLeft(maxTargetLength)}` {EggIncStatics.GetReward(x)}"));
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
                    _logger.LogInformation("Deleting header channels for {contract} because the contract is expired.", guildContract.Contract.Name);
                    await dbGuild.DeleteCoopThreadHeadersAsync(_client, guildContract.Contract, _logger);
                    guildContract.DeletedChannel = true;

                    if(guildContract.Contract.MaxUsers > 1 && guildContract.GuildID == 656455567858073601 && guildContract.Created > DateTimeOffset.Now.AddMonths(-3) && !guildContract.HasScores) {
                        var farmersUnion = guild.GetTextChannel(777303939442802710); //#farmers-union
                        farmersUnion ??= await _client.GetChannelAsync(777303939442802710) as SocketTextChannel;
                        if(farmersUnion != null) {
                            await farmersUnion.SendMessageAsync(text: "", embed: EmbedHelpers.EmbedAlert($"The contract `{guildContract.Contract.GetE9KName(false)}` has finished, and is ready to be scored."));
                        }
                    }

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
                var findSpotButton = (bgsLaunched && guildContract.Contract.GoodUntil > DateTimeOffset.Now && guildContract.Contract.ContractTime >= TimeSpan.FromHours(NewContracts.MIN_HOURS_TO_CREATE_COOPS)) ? new ComponentBuilder().WithButton("Find Coop Spot", customId: $"FindCoopSpot").Build() : null;
#endif

                existingMessages = [.. existingMessages.Where(x => x.Author.IsBot).OrderBy(x => x.CreatedAt)];

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
            return value.Length <= maxLength ? value : value[..maxLength];
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
