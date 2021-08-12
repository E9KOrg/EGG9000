using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Common.Helpers {
    public class CreateCoops {
        public static async Task<Coop> Start(List<UserPreFarm> prefarms, GuildContract guildContract, SocketGuild guild, Words _words, ApplicationDbContext db) {
            var discordUsers = prefarms.Select(x => guild.GetUser(x.DiscordId));
            var coop = new Coop();

            coop.ContractID = guildContract.ContractID;
            coop.Created = DateTimeOffset.Now;
            coop.GuildId = guild.Id;
            coop.Name = _words.GetCoopName(discordUsers);
            coop.MaxUsers = guildContract.Contract.MaxUsers;
            coop.Status = CoopStatusEnum.WaitingOnAssigned;
            coop.League = (UInt32)(guildContract.Elite ? 0 : 1);
            coop.CoopEnds = DateTimeOffset.Now.AddSeconds(guildContract.Contract.Details.LengthSeconds);


            db.Coops.Add(coop);


            foreach(var user in prefarms) {
                db.UserCoopXrefs.Add(new UserCoopXref {
                    AddedToChannel = false,
                    CreatedOn = DateTimeOffset.Now,
                    CoopId = coop.Id,
                    JoinedCoop = false,
                    WaitingOnStarter = false,
                    UserId = user.DatabaseId.Value,
                    EggIncId = user.EggIncId,
                    WasAssigned = true
                });
            }


            var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(new Ei.CreateCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = guildContract.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                League = (uint)(guildContract.Elite ? 0 : 1),
                Platform = Aux.Platform.Ios,
                SecondsRemaining = Math.Max(guildContract.Contract.Details.LengthSeconds, 131072),
                //SecondsRemaining = (uint)guildContract.Contract.Details.LengthSeconds,
                SoulPower = guildContract.Elite ? 24.24559831915049 : 8.75,
                UserId = ContractsAPI.UserId,
                UserName = "EK9"
            }, ContractsAPI.UserId);

            var r = await ContractsAPI.Send<Ei.KickPlayerCoopRequest>(new Ei.KickPlayerCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                PlayerIdentifier = ContractsAPI.UserId,
                Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                RequestingUserId = ContractsAPI.UserId
            }, ContractsAPI.UserId);

            return coop;
        }

        public static async Task<UserCoopXref> MoveUser(Coop targetCoop, Guid dbuserid, String EggIncId, String eggIncName, SocketUser user, DBUser dbuser, SocketTextChannel targetChannel, SocketTextChannel commandChannel) {
            var newxref = new UserCoopXref {
                AddedToChannel = true,
                CoopId = targetCoop.Id,
                CreatedOn = DateTimeOffset.Now,
                JoinedCoop = false,
                Starter = false,
                UserId = dbuserid,
                WaitingOnStarter = false,
                EggIncId = EggIncId,
                WasAssigned = true
            };


            var eggEmoji = EggIncEggs.GetEggById((int)targetCoop.Contract.Details.Egg).Emoji;
            var mention = user.Mention;
            if(dbuser.EggIncIds.Count > 1) {
                mention += $"({eggIncName})";
            }
            try {
                await ((SocketTextChannel)targetChannel).AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
            } catch(Exception) {
                try {
                    await ((SocketTextChannel)commandChannel).SendMessageAsync($"Moved but unable to add permssions for {mention} to {targetChannel.Mention} for the contract {eggEmoji} {targetCoop.Contract.Name}. {(commandChannel.Guild.Id != targetChannel.Guild.Id ? "Might not be in overflow server." : "")}");
                } catch(Exception e) {
                    await ((SocketTextChannel)targetChannel).SendMessageAsync($"Added {mention}, please join {targetCoop.Name} for the contract {targetCoop.Contract.Name}");
                    return newxref;
                }
            }


            await ((SocketTextChannel)targetChannel).SendMessageAsync($"Please join {targetCoop.Name} {mention} for the contract {eggEmoji} {targetCoop.Contract.Name}");
            return newxref;
        }
    }
}
