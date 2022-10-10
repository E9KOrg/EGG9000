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
using Microsoft.EntityFrameworkCore;
using Polly;

namespace EGG9000.Common.Helpers {
    public class CreateCoops {
        public static async Task<Coop> Start(List<UserFarmDetails> prefarms, GuildContract guildContract, SocketGuild guild, Words _words, ApplicationDbContext db) {
            //var discordUsers = prefarms.Select(x => guild.GetUser(x.DiscordId));


            var secondsRemaining = guildContract.Contract.Details.LengthSeconds;

            if(guildContract.Contract.GoodUntil < DateTimeOffset.Now)
                secondsRemaining = (guildContract.Contract.GoodUntil.AddSeconds(guildContract.Contract.Details.LengthSeconds) - DateTimeOffset.Now).TotalSeconds;

            var coopEnds = DateTimeOffset.Now.AddSeconds(secondsRemaining);

            var coop = new Coop();
            var dbguild = await db.Guilds.FirstAsync(x => x.Id == guildContract.GuildID);
            coop.ContractID = guildContract.ContractID;
            coop.Created = DateTimeOffset.Now;
            coop.GuildId = guild.Id;
            coop.Name = _words.GetCoopName(prefarms, guild, dbguild);
            coop.MaxUsers = guildContract.Contract.MaxUsers;
            coop.Status = CoopStatusEnum.WaitingOnAssigned;
            coop.League = (UInt32)(guildContract.Elite ? 0 : 1);
            coop.CoopEnds = coopEnds;


            db.Coops.Add(coop);


            foreach(var user in prefarms) {
                db.UserCoopXrefs.Add(new UserCoopXref {
                    AddedToChannel = false,
                    CreatedOn = DateTimeOffset.Now,
                    CoopId = coop.Id,
                    JoinedCoop = false,
                    WaitingOnStarter = false,
                    UserId = user.DBUser.Id,
                    EggIncId = user.EggIncId,
                    WasAssigned = true
                });
            }

            //var coopLength = Math.Max(guildContract.Contract.Details.LengthSeconds, 131072);

            //if(guildContract.Contract.GoodUntil < DateTimeOffset.Now) {
            //    coopLength -= Math.Abs((DateTimeOffset.Now - guildContract.Contract.GoodUntil).TotalSeconds);
            //}


            var id = prefarms.FirstOrDefault(x => x.Backup is not null)?.Backup.EggIncId;

            await CreateCoops.CreateCoopViaApi(guildContract.ContractID, (uint)(guildContract.Elite ? 0 : 1), coop, secondsRemaining, id);


            return coop;
        }



        public static async Task<bool> CreateCoopViaApi(string ContractID, uint League, Coop coop, double secondsRemaining, string userid) {
            //userid = userid ?? ContractsAPI.UserId;
            userid = ContractsAPI.UserId;
            var policy = Policy
              .Handle<Exception>()
              .WaitAndRetry(new[]
              {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(7)
              });

            try {
                await policy.Execute(async () => await _CreateCoop(ContractID, League, coop, secondsRemaining, userid));
            } catch (Exception) {
                return false;
            }

            //var response = await _CreateCoop(ContractID, League, coop, secondsRemaining);

            var r = await ContractsAPI.Send<Ei.KickPlayerCoopRequest>(new Ei.KickPlayerCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = coop.ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                PlayerIdentifier = userid,
                Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                RequestingUserId = userid
            }, userid);

            return true;
        }
        private static async Task<Ei.CreateCoopResponse> _CreateCoop(string ContractID, uint League, Coop coop, double secondsRemaining, string userid) {
            var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(new Ei.CreateCoopRequest {
                ClientVersion = ContractsAPI.ClientVersion,
                ContractIdentifier = ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                League = League,
                Platform = Aux.Platform.Ios,
                SecondsRemaining = secondsRemaining,
                //SecondsRemaining = (uint)guildContract.Contract.Details.LengthSeconds,
                SoulPower = League == 0 ? 24.24559831915049 : 8.75,
                UserId = userid,
                UserName = "EK9"
            }, userid);
            if(response == null) {
                throw new Exception();
            }
            return response;
        }


        public static async Task<UserCoopXref> MoveUser(Coop targetCoop, Guid dbuserid, String EggIncId, String eggIncName, IUser user, DBUser dbuser, SocketTextChannel targetChannel, SocketTextChannel commandChannel) {
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
                    await ((SocketTextChannel)commandChannel).SendMessageAsync(commandChannel.Guild.Id != targetChannel.Guild.Id ? $"{mention} looks like you are not in the overflow servers. **Make sure and join the overflow servers in <#775558629671698442> to see your co-op, it's in {targetChannel.Guild.Name}**." : "Looks like an error happened, please use /callstaff");
                } catch(Exception) {
                    await ((SocketTextChannel)targetChannel).SendMessageAsync($"Added {mention}, please join {targetCoop.Name} for the contract {targetCoop.Contract.Name}");
                    return newxref;
                }
            }


            await ((SocketTextChannel)targetChannel).SendMessageAsync($"Please join {targetCoop.Name} {mention} for the contract {eggEmoji} {targetCoop.Contract.Name}");
            return newxref;
        }
    }
}
