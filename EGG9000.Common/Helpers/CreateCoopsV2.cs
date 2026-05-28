using Discord;
using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

using Polly;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class CreateCoopsV2 {

        //Overflow moved from 500 -> 495 to account for any ghosting like we saw with Overflow 1
        public const int PrimaryMaxChannels = 450;
        public const int OverflowMaxChannels = 495;

        //Leave above 970 so Discord does it's auto-pruning
        //Overflow moved from 1000 -> 995 to account for any ghosting like we saw with Overflow 1
        public const int PrimaryMaxThreads = 975;
        public const int OverflowMaxThreads = 995;

        public static async Task<Coop> Start(List<UserByAccount> accounts, Contract contract, Ei.Contract.Types.PlayerGrade grade, SocketGuild guild, Words words, IServiceProvider provider, Guild dbGuild, uint Group, bool allowAllGrades) {
            var db = provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();


            string creatorId = null;

            if(ContractsAPI.CoopCreatorIds.Any(x => x.Grade == grade) && !allowAllGrades) {
                creatorId = ContractsAPI.CoopCreatorIds.First(x => x.Grade == grade).EggIncId;
            } else {

                foreach(var account in accounts.OrderByDescending(a => a?.Account?.LastGrade)) {
                    var r = await ContractsAPI.Post<Ei.ContractPlayerInfo, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), account.Account.Id);
                    if(r?.Grade == grade) {
                        creatorId = account.Account.Id;
                        break;
                    }
                }

                if(string.IsNullOrEmpty(creatorId)) {
                    var account = accounts.OrderByDescending(x => x.Account.Backup.LastBackupTime).First();
                    creatorId = account.Account.Id;
                    //GetLogger<CreateCoopsV2>().LogCritical("Unable to find a user in the grade {grade} to be able to create co-op with the users {users}", grade, String.Join(",", accounts.Select(x => x.User.DiscordUsername)));
                    //throw new Exception($"Unable to a find user in the grade {grade}");
                }
            }

            var secondsRemaining = Math.Max(contract.Details.LengthSeconds, TimeSpan.FromDays(1.6).TotalSeconds);
            var coopEnds = DateTimeOffset.Now.AddSeconds(secondsRemaining);

            var coop = new Coop {
                ContractID = contract.ID,
                Created = DateTimeOffset.Now,
                GuildId = guild.Id,
                Name = words.GetCoopName(accounts, guild, dbGuild),
                MaxUsers = contract.MaxUsers,
                Status = CoopStatusEnum.WaitingOnCreation,
                League = (uint)grade,
                AnyLeague = allowAllGrades,
                CoopEnds = coopEnds,
                CreatorID = creatorId,
                Group = Group
            };

            db.Coops.Add(coop);

            foreach(var user in accounts) {
                db.UserCoopXrefs.Add(new UserCoopXref {
                    AddedToChannel = false,
                    CreatedOn = DateTimeOffset.Now,
                    CoopId = coop.Id,
                    JoinedCoop = false,
                    WaitingOnStarter = false,
                    UserId = user.User.Id,
                    EggIncId = user.Account.Id,
                    WasAssigned = true,
                    Group = user.Group
                });
            }

            //var coopLength = Math.Max(guildContract.Contract.Details.LengthSeconds, 131072);

            //if(guildContract.Contract.GoodUntil < DateTimeOffset.Now) {
            //    coopLength -= Math.Abs((DateTimeOffset.Now - guildContract.Contract.GoodUntil).TotalSeconds);
            //}


            await db.SaveChangesAsync();
            return coop;
        }



        public static async Task<bool> CreateCoopViaApi(string ContractID, Ei.Contract.Types.PlayerGrade grade, string coopName, double secondsRemaining, string userId, bool allowAllGrades, bool kickCreator = true, TimingsFactory timings = null) {
            userId ??= ContractsAPI.UserId;
            var policy = Policy
              .Handle<Exception>()
              .WaitAndRetry(
              [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(7)
              ]);

            // Coop may have been created manually. Check before attempting creation, since _CreateCoop overwrites an existing coop and kicks the creator
            var existingStatus = await ContractsAPI.GetCoopStatusBot(ContractID, coopName);
            timings?.Set("Get Coop Status");
            if(existingStatus is not null && existingStatus.ResponseStatus == Ei.ContractCoopStatusResponse.Types.ResponseStatus.NoError) {
                return true;
            }

            try {
                await policy.Execute(async () => await _CreateCoop(ContractID, grade, coopName, secondsRemaining, userId, allowAllGrades));
                timings?.Set("Create Coop");
            } catch(Exception) {
                return false;
            }

            //var response = await _CreateCoop(ContractID, League, coop, secondsRemaining);



            var res = new Ei.ContractCoopStatusUpdateRequest {
                ContractIdentifier = ContractID,
                CoopIdentifier = coopName.ToLower(),
                Eop = 1, SoulPower = 24, UserId = userId, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = userId, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                ProductionParams = new Ei.FarmProductionParams {
                    FarmPopulation = 0, Delivered = 0, Elr = 0, FarmCapacity = 0, Ihr = 0, Sr = 0
                }
            };


            var response = await ContractsAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(res, res.UserId, true);
            timings?.Set("CoopStatusUpdate");


            if(kickCreator) {
                var r = await ContractsAPI.Send<Ei.KickPlayerCoopRequest>(new Ei.KickPlayerCoopRequest {
                    ClientVersion = ContractsAPI.ClientVersion,
                    ContractIdentifier = ContractID,
                    CoopIdentifier = coopName.ToLower(),
                    PlayerIdentifier = userId,
                    Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                    RequestingUserId = userId
                }, userId);
                timings?.Set("Kick Creator");
            }

            return true;
        }
        private static async Task<Ei.CreateCoopResponse> _CreateCoop(string ContractID, Ei.Contract.Types.PlayerGrade grade, string coopName, double secondsRemaining, string userid, bool allowAllGrades) {
            var userName = userid;

            if(ContractsAPI.CoopCreatorIds.Any(x => x.EggIncId == userid)) {
                userName = $"E9K-{grade}";
            }

            var request = new Ei.CreateCoopRequest {
                ContractIdentifier = ContractID,
                CoopIdentifier = coopName.ToLower(),
                SecondsRemaining = secondsRemaining,
                UserId = userid,
                UserName = userName,
                Platform = Ei.Platform.Droid,
                ClientVersion = ContractsAPI.ClientVersion,
                SoulPower = 4624103542699216300,
                Eop = 4632655904192331776,
                Grade = grade,
                AllowAllGrades = allowAllGrades,
            };

            var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(request, userid);

            if(response is null || response.Success == false) {
                throw new Exception($"Unable to create co-op for {coopName}: {response?.Message ?? "Null response"}");
            }

            return response;
        }


        public static async Task<UserCoopXref> MoveUser(Coop targetCoop, Guid dbUserId, string EggIncId, string eggIncName, ApplicationDbContext db, IUser user, DBUser dbUser, SocketTextChannel targetChannel, SocketTextChannel commandChannel, bool silent = false) {
            var newxref = new UserCoopXref {
                AddedToChannel = true,
                CoopId = targetCoop.Id,
                CreatedOn = DateTimeOffset.Now,
                JoinedCoop = false,
                Starter = false,
                UserId = dbUserId,
                WaitingOnStarter = false,
                EggIncId = EggIncId,
                WasAssigned = true
            };

            var eggEmoji = EggIncStatics.GetEggById(targetCoop.Contract.Details.Egg, targetCoop.Contract, await db.GetCustomEggsAsync()).emoji;
            var mention = user.Mention;
            if(dbUser.EggIncAccounts.Count > 1) {
                mention += $"({eggIncName})";
            }
            try {
                if(targetChannel.GetChannelType() != ChannelType.PrivateThread) {
                    await targetChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
                }
            } catch(Exception) {
                try {
                    await commandChannel.SendMessageAsync(commandChannel.Guild.Id != targetChannel.Guild.Id ? $"{mention} looks like you are not in the overflow servers. **Make sure and join the overflow servers in <#775558629671698442> to see your co-op, it's in {targetChannel.Guild.Name}**." : "Looks like an error happened, please use /callstaff");
                } catch(Exception) {
                    if(!silent) await targetChannel.SendMessageAsync($"Added {mention}, please join {targetCoop.Name} for the contract {targetCoop.Contract.Name}");
                    return newxref;
                }
            }

            //Always ping when it's a Thread - this is how users are added to the channel
            if(!silent || targetChannel.GetChannelType() == ChannelType.PrivateThread) await targetChannel.SendMessageAsync($"Please join {targetCoop.Name} {mention} for the contract {eggEmoji} {targetCoop.Contract.Name}");
            return newxref;
        }
    }
}
