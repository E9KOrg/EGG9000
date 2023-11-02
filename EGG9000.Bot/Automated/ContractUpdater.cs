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

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
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
                //_ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                //await ShipReturnDM.UpdateNextShipDM(dbusers, _db, _logger);
#else
                _ = await _apiLink.GetUserBackups(dbusers, _db, cancellationToken);
                await ShipReturnDM.UpdateNextShipDM(dbusers, _db, _logger);
#endif

                var groupGuildContracts = guildGroups.FirstOrDefault(x => x.Key == dbguild.DiscordSeverId);
                //var contractIds = groupGuildContracts.Select(x => x.ContractID);




                var dbguild = dbGuilds.First(x => x.Id == guild.Id);
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
                    var contract = contracts.First(x => x.ID == pCoop.contractid);
                    var exisitingCoop = await _db.Coops.FirstOrDefaultAsync(x => x.ContractID == pCoop.contractid && EF.Functions.Like(x.Name, pCoop.coopname));
                    if(exisitingCoop is not null) {
                        _logger.LogInformation("Co-op {coopname} already exists, skipping", pCoop.coopname);
                        continue;
                    }
                    _logger.LogInformation("Adding co-op {coopname} from backups", pCoop.coopname);
                    var coop = new Coop {
                        ContractID = pCoop.contractid, Created = DateTimeOffset.Now, GuildId = pCoop.guildid, Name = pCoop.coopname,
                        MaxUsers = contract.MaxUsers, Status = CoopStatusEnum.WaitingOnAssigned, League = pCoop.grade,
                        CoopEnds = DateTimeOffset.FromUnixTimeSeconds(pCoop.endtime)
                    };
                    coops.Add(new { Name = coop.Name });
                    _db.Add(coop);
                    await _db.SaveChangesAsync();
                    count++;
                }

            }

            await _db.SaveChangesAsync();
        }

        public async Task UpdateContractChannel(ApplicationDbContext _db, GuildContract guildContract, SocketGuild guild,  Guild dbGuild, FauxCommand slashCommand = null) {
            try {
                _logger.LogInformation("Working on GuildContract for {guild} - {contract}", guild.Name, guildContract.Contract.Name);


                var channel = guild.TextChannels.FirstOrDefault(x => x.Id == guildContract.DiscordChannelId);


                if(guildContract.Contract.GoodUntil.AddSeconds(guildContract.Contract.Details.LengthSeconds).AddDays(1) < DateTimeOffset.Now) {
                    if(channel != null) {
                        await channel.DeleteAsync();
                    }
                    guildContract.DeletedChannel = true;
                    await _db.SaveChangesAsync();
                    return;
                }


                if(channel == null) {
                    _logger.LogWarning("Missing Channel for {contract} in {guild}", guildContract.Contract.Name, guild.Name);
                    return;
                }

                await UpdateContractChannelName(guildContract, channel, guild);

                var validFor = (DateTimeOffset.FromUnixTimeSeconds((long)guildContract.Contract.Details.ExpirationTime) - DateTime.Now);
                var league = guildContract.League;

                var newMsgs = new List<string>();

                var description = $"**Size** {guildContract.Contract.Details.MaxCoopSize}, **<:Token_boost:724397091211968604>** {guildContract.Contract.Details.MinutesPerToken}mins,";
                description += $"**{(validFor > TimeSpan.Zero ? "  Expires " : " Expired ")}** {DiscordHelpers.TimeStamper(validFor)}";
                //description += $"\n[View Co-ops on egg9000.com](https://egg9000.com/Contract/Details?GuildId={guild.Id}&ContractId={guildContract.ContractID}&League={guildContract.League})";
                if(guildContract.BoardingGroup < 3)
                    description += $"\n[View Upcoming Co-ops on egg9000.com](https://egg9000.com/Contract/Day1CoopsFillLate?GuildId={guild.Id}&ContractId={guildContract.ContractID})";

                var embedBuilder = new EmbedBuilder()
                    .WithDescription(description)
                    .WithAuthor(
                        new EmbedAuthorBuilder().WithName($"{guildContract.Contract.Name} - {guildContract.Contract.ID}")
                        .WithIconUrl(EggIncEggs.GetEggById((int)guildContract.Contract.Details.Egg).Image));


                for(var i = 5; i >=  1; i--) {
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

                var condensedMsgs = new List<string>();

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

                var findSpotButton = (dbGuild.DisableBG && dbGuild.Id != 1108127105088241746 /*DEV server*/) ? null : ((DateTimeOffset.Now > guildContract.Contract.Created.AddHours(guildContract.CcOnly ? 24 : 18)) ? new ComponentBuilder().WithButton("Find Coop Spot", customId: $"FindCoopSpot").Build() : null);

                existingMessages = existingMessages.Where(x => x.Author.IsBot).OrderBy(x => x.CreatedAt).ToList();

                if(existingMessages.Count > 0) {
                    await (existingMessages.First() as RestUserMessage).ModifyWithTimeoutAsync(msg => {
                        msg.Embed = embedBuilder.Build();
                        msg.Content = rawTextMessageAspect;
                        msg.Components = findSpotButton;
                    });
                } else {
                    await channel.SendMessageAsync(rawTextMessageAspect, embed: embedBuilder.Build());
                }


            } catch(Exception e) {
                _logger.LogError(e, "Error Updating Contracts Channel");
                _bugsnag.Notify(e);
            }

        }

        //public void ShowCurrentCoops(GuildContract guildContract, List<CoopDetails> coopsDetails, SocketTextChannel channel, List<string> newMsgs, double targetAmount, bool allStarted) {
        //    if(guildContract.Status != ContractStatus.Completed && coopsDetails.All(x => x.Coop.Status == CoopStatusEnum.Completed || x.Coop.Finished) && allStarted) {
        //        guildContract.Status = ContractStatus.Completed;
        //    }

        //    for(var i = 1; i <= coopsDetails.Count; i++) {
        //        var coopDetails = coopsDetails[i - 1];

        //        if(coopDetails.Coop.DeletedChannel || !coopDetails.HasSpots || coopDetails.Coop.Finished)
        //            continue;

        //        if(coopDetails.Coop.Finished && coopDetails.Coop.Status != CoopStatusEnum.Completed) {
        //            coopDetails.Coop.Status = CoopStatusEnum.Completed;
        //        }


        //        var name = coopDetails.Coop.Status switch {
        //            CoopStatusEnum.WaitingOnStarter => $"Coop {i} - Waiting on bot to start",
        //            CoopStatusEnum.WaitingOnAssigned => $"Coop {i} - Waiting on users",
        //            CoopStatusEnum.AllAssignedJoined => $"Coop {i}",
        //            CoopStatusEnum.Full => $"Coop {i}",
        //            CoopStatusEnum.Completed => $"Coop {i} completed! 🎆",
        //            _ => ""
        //        };

        //        if(!coopDetails.Coop.Finished) {
        //            if(coopDetails.TimeRemaining > TimeSpan.Zero) {
        //                name += $" Completes: {coopDetails.TimeRemaining.Humanize(2).ShortenTime()}";
        //            }
        //            if(coopDetails.Coop.CoopEnds < DateTimeOffset.Now) {
        //                name += $" Expired: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()} ago";
        //            } else if((coopDetails.Coop.CoopEnds - DateTimeOffset.Now) < coopDetails.TimeRemaining) {
        //                name += $" Expires: {(coopDetails.Coop.CoopEnds - DateTimeOffset.Now).Value.Humanize(2).ShortenTime()}";
        //            }
        //        }

        //        var userIds = coopDetails.Coop.UserCoopsXrefs.Select(x => x.GetID());



        //        newMsgs.AddRange(ShowCoopStatus(coopDetails, name, targetAmount, guildContract.Contract.Details.MaxCoopSize));
        //    }
        //}

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

//        private List<string> ShowCoopStatus(CoopDetails coop, string coopName, double target, uint size, bool alreadyInCoop = false) {
//            var table = new List<List<FixedWidthCell>> {
//                new List<FixedWidthCell> {
//                    new FixedWidthCell("Name", CellAlignment.Center),
//                    new FixedWidthCell("🐔", CellAlignment.Center),
//                    new FixedWidthCell("🥚", CellAlignment.Center),
//                    new FixedWidthCell("📈", CellAlignment.Center, true),
//                    new FixedWidthCell(coop.CoopParticipants.All(x => string.IsNullOrEmpty(x.Farm?.CoopId)) ? "🟡" : "")
//                }
//            };
//            table.AddRange(coop.CoopParticipants.OrderByDescending(x => x.Projected).Select(x => {
//                var timeleft = "";
//                if(x.TimeLeft.TotalSeconds > 0) {
//                    timeleft = x.TimeLeft.Humanize(precision: 2).ShortenTime();
//                } else {
//                    timeleft = "Time has run out";
//                }
//                var emoji = "";
//                if(x.DiscordUser != null && x.DiscordUser.Roles.Any(r => r.Id == 796512753241161748)) {
//                    emoji = "🆕";
//                }
//                return new List<FixedWidthCell> {
//                    new FixedWidthCell(Truncate($"{emoji}{x.Name}", 12)),
//                    //new FixedWidthCell(x.EggsPaidFor.ToEggString()),
//                    new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
//                    new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false, -1) + "/h", CellAlignment.Right),
//                    new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
//                    //new FixedWidthCell(x.Tokens.ToString()),
//                    //new FixedWidthCell(x.BoostTokensSpent.ToString()),
//                    //new FixedWidthCell(x.TimeSinceUpdate.Humanize(1, minUnit: Humanizer.Localisation.TimeUnit.Minute).ShortenTime()),
//                    new FixedWidthCell(GetCoopStatus(coop.Coop, x, alreadyInCoop))
//                };
//            }));

//            if(coopName != "Expired Farms" && coopName != "Already in coop") {
//                var percent = $"{coop.PercentProjected / 100:P0}".Replace(",", ""); //$"{coop.Users.Sum(x => x.Projected) / target:P0}";

//                table.Add(new List<FixedWidthCell> {
//                    new FixedWidthCell($"{coop.CoopParticipants.Count}/{size}"),
//                    new FixedWidthCell(""),
//                    new FixedWidthCell(ArgumentsHelper.NumberToString(coop.CoopParticipants.Sum(x => x.Rate) * 60 * 60, false, -1) + "/h", CellAlignment.Right),
//                    //new FixedWidthCell(""),
//                    new FixedWidthCell(coop.CoopParticipants.Sum(x => x.Projected).ToEggString(), CellAlignment.Right),
//                    new FixedWidthCell(percent, CellAlignment.Right),
//                    new FixedWidthCell(""),
//                    new FixedWidthCell(""),
//                    new FixedWidthCell("")
//                });
//            }



//            var tableString = $"{coopName}\n```{GetTable(table)}```\n";
//            //var startLength = tableString.Length;
//            //tableString = tableString.Replace("  ", "\t");
//            var msgs = new List<string>();
//            while(tableString.Length > 2000) {
//                var index = tableString.LastIndexOf('\n', 2000);
//                msgs.Add(tableString[..index] + "```");
//                tableString = "```" + tableString[index..];
//            }
//            msgs.Add(tableString);
//            return msgs;
//        }

        //private static string GetCoopStatus(Coop coop, UserFarmDetails user, bool alreadyInCoop) {
        //    if(alreadyInCoop)
        //        return $"[{user.Farm?.CoopId ?? user.ArchivedFarm?.CoopId}] Joined {(DateTimeOffset.Now - user.DBUser.Registered.Value).Humanize().ShortenTime()} ago";
        //    if(coop is not null && user.InCoop && user.DBUser is null)
        //        return "👽";
        //    if(coop is not null && user.InCoop)
        //        return "✔️";
        //    if(coop is not null && !user.InCoop)
        //        return $"❌ {user.FarmExpires.Humanize().ShortenTime()}";
        //    if(!string.IsNullOrEmpty(user.Farm?.CoopId))
        //        return user.Farm.CoopId;
        //    if(user.Farm is not null)
        //        return (user.Farm.BoostTokensReceived - user.Farm.BoostTokensGiven - user.Farm.BoostTokensSpent).ToString();
        //    return "?";
        //}

        //private List<string> ShowSoloStatus(CoopsBreakdown coopsBreakdown, Ei.Contract contract, double target) {
        //    var table = new List<List<FixedWidthCell>> {
        //        new List<FixedWidthCell> {
        //            new FixedWidthCell("Name", CellAlignment.Center),
        //            new FixedWidthCell("🐔", CellAlignment.Center),
        //            new FixedWidthCell("🥚", CellAlignment.Center),
        //            new FixedWidthCell("📈", CellAlignment.Center, true),
        //            new FixedWidthCell(""),
        //            new FixedWidthCell("⏲️", CellAlignment.Center)
        //        }
        //    };

        //    var participants = coopsBreakdown.PotentialCoops.SelectMany(x => x.CoopParticipants).ToList();
        //    participants.AddRange(coopsBreakdown.ExpiredFarms);


        //    table.AddRange(participants.Where(x => x.NumChickens > 0).OrderBy(x => x.Name).Select(x => {
        //        return new List<FixedWidthCell> {
        //            new FixedWidthCell(Truncate(x.Name, 12)),
        //            new FixedWidthCell(x.NumChickens.ToEggString(), CellAlignment.Right),
        //            new FixedWidthCell(ArgumentsHelper.NumberToString(x.Rate * 60 * 60, false, -1) + "/h", CellAlignment.Right),
        //            new FixedWidthCell(x.Projected.ToEggString(), CellAlignment.Right),
        //            new FixedWidthCell(string.Format("{0:0%}", x.Projected/target) , CellAlignment.Right),
        //            new FixedWidthCell(x.EggsShipped < target ?  GetTimeRemainingValue(target, x.Rate, x.EggsShipped).Humanize(1, null, Humanizer.Localisation.TimeUnit.Year).ShortenTime() : "Finished", CellAlignment.Right)
        //        };
        //    }));


        //    var tableString = $"```{FixedWidthTable.GetTable(table)}```";
        //    var msgs = new List<string>();
        //    while(tableString.Length > 2000) {
        //        var index = tableString.LastIndexOf('\n', 2000);
        //        msgs.Add(string.Concat(tableString.AsSpan(0, index), "```"));
        //        tableString = string.Concat("```", tableString.AsSpan(index));
        //    }
        //    msgs.Add(tableString);
        //    return msgs;
        //}

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
