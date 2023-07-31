using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;

using Polly;
using EGG9000.Common.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Common.Helpers {
    public class CreateCoopsV2 {
        public static async Task<Coop> Start(List<UserByAccount> accounts, Contract contract, Ei.Contract.Types.PlayerGrade grade, SocketGuild guild, Words words, IServiceProvider provider, Guild dbguild, uint Group) {
            var db = provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();

            string EIID = null;

            foreach(var account in accounts) {
                var r = await ContractsAPI.Post<Ei.ContractPlayerInfo, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), account.Account.Id);
                if(r?.Grade == grade) {
                    EIID = account.Account.Id;
                    break;
                }
            }

            if(string.IsNullOrEmpty(EIID)) {
                var account = accounts.OrderByDescending(x => x.Account.Backup.LastBackupTime).First();
                EIID = account.Account.Id;
                //GetLogger<CreateCoopsV2>().LogCritical("Unable to find a user in the grade {grade} to be able to create co-op with the users {users}", grade, String.Join(",", accounts.Select(x => x.User.DiscordUsername)));
                //throw new Exception($"Unable to a find user in the grade {grade}");
            }

            var secondsRemaining = Math.Max(contract.Details.LengthSeconds, TimeSpan.FromDays(1.6).TotalSeconds);
            var coopEnds = DateTimeOffset.Now.AddSeconds(secondsRemaining);

            var coop = new Coop {
                ContractID = contract.ID,
                Created = DateTimeOffset.Now,
                GuildId = guild.Id,
                Name = words.GetCoopName(accounts, guild, dbguild),
                MaxUsers = contract.MaxUsers,
                Status = CoopStatusEnum.WaitingOnAssigned,
                League = (uint)grade,
                AnyLeague = contract.cc_only,
                CoopEnds = coopEnds,
                CreatorID = EIID,
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

            await CreateCoopViaApi(contract.ID, grade, coop, secondsRemaining, EIID, contract.cc_only);

            await db.SaveChangesAsync();
            return coop;
        }



        public static async Task<bool> CreateCoopViaApi(string ContractID, Ei.Contract.Types.PlayerGrade grade, Coop coop, double secondsRemaining, string userid, bool subOnly = false) {
            userid ??= ContractsAPI.UserId;
            var policy = Policy
              .Handle<Exception>()
              .WaitAndRetry(new[]
              {
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(7)
              });

            try {
                await policy.Execute(async () => await _CreateCoop(ContractID, grade, coop, secondsRemaining, userid, subOnly));
            } catch(Exception) {
                return false;
            }

            //var response = await _CreateCoop(ContractID, League, coop, secondsRemaining);

            //var s = "ChJFSTUyMjMyOTk1MTgzMDAxNjASC2EtbmV3LWdyYWRlGgZ0ZXN0MzQhAAAAAAAAAAApAAAAAAAAAAAwADmsTTYrrB0sQEIkODMxNWRhYmQtZThlNy00ZmUyLWFiYTAtZTBlMjEwOWNhZmIySABRAAAAAAAA8D9ZAAAAAAAA8D9iNQoSRUk1MjIzMjk5NTE4MzAwMTYwEC4aBDEuMjYiCDEuMjYuMC41KgNJT1MyAlVTOgJlbkAAaAByNgkAAAAAAAAAABEAAAAAAEBvQBkL16NwPQqnPyEAAAAAAAAAACmrqqqqqupWQDEAAAAAAAAAAHg1ggHBDgkAwEFHlmFcQhA1GAAgASgFKAUoBSgFKAUoBSgFKAUoBSgFKAUoBSgFKAUoBSgFKAUoBSgFMhEKDWhvbGRfdG9faGF0Y2gQDzIRCg1lcGljX2hhdGNoZXJ5EBQyHAoYZXBpY19pbnRlcm5hbF9pbmN1YmF0b3JzEBQyFgoSdmlkZW9fZG91Ymxlcl90aW1lEAEyEQoNZXBpY19jbHVja2luZxAIMhMKD2VwaWNfbXVsdGlwbGllchALMhcKE2NoZWFwZXJfY29udHJhY3RvcnMQADIPCgtidXN0X3VuaW9ucxAAMhQKEGNoZWFwZXJfcmVzZWFyY2gQCjIVChFlcGljX3NpbG9fcXVhbGl0eRAAMhEKDXNpbG9fY2FwYWNpdHkQFDIVChFpbnRfaGF0Y2hfc2hhcmluZxAAMhIKDmludF9oYXRjaF9jYWxtEBQyFQoRYWNjb3VudGluZ190cmlja3MQBzIUChBob2xkX3RvX3Jlc2VhcmNoEAAyDgoJc291bF9lZ2dzEIwBMhIKDnByZXN0aWdlX2JvbnVzEBQyEQoNZHJvbmVfcmV3YXJkcxAAMhMKD2VwaWNfZWdnX2xheWluZxAHMhsKF3RyYW5zcG9ydGF0aW9uX2xvYmJ5aXN0EAIyDgoKd2FycF9zaGlmdBAAMhIKDnByb3BoZWN5X2JvbnVzEAUyFAoQYWZ4X21pc3Npb25fdGltZRAAMhgKFGFmeF9taXNzaW9uX2NhcGFjaXR5EAA4AUEAAAAAAAAAAEgASBNIE0gTUABQAFAAUABYAGABaAFyDwoLY29tZnlfbmVzdHMQAHITCg9udXRyaXRpb25hbF9zdXAQAHIVChFiZXR0ZXJfaW5jdWJhdG9ycxAAchYKEmV4Y2l0YWJsZV9jaGlja2VucxAAchEKDWhhYl9jYXBhY2l0eTEQAHIWChJpbnRlcm5hbF9oYXRjaGVyeTEQAHIUChBwYWRkZWRfcGFja2FnaW5nEAByFgoSaGF0Y2hlcnlfZXhwYW5zaW9uEAByDwoLYmlnZ2VyX2VnZ3MQAHIWChJpbnRlcm5hbF9oYXRjaGVyeTIQAHIPCgtsZWFmc3ByaW5ncxAAchYKEnZlaGljbGVfcmVsaWFibGl0eRAAchMKD3Jvb3N0ZXJfYm9vc3RlchAAchgKFGNvb3JkaW5hdGVkX2NsdWNraW5nEAByFQoRaGF0Y2hlcnlfcmVidWlsZDEQAHIOCgp1c2RlX3ByaW1lEAByEAoMaGVuX2hvdXNlX2FjEAByDQoJc3VwZXJmZWVkEAByDAoIbWljcm9sdXgQAHIWChJjb21wYWN0X2luY3ViYXRvcnMQAHIVChFsaWdodHdlaWdodF9ib3hlcxAAchEKDWV4Y29za2VsZXRvbnMQAHIWChJpbnRlcm5hbF9oYXRjaGVyeTMQAHIVChFpbXByb3ZlZF9nZW5ldGljcxAAchYKEnRyYWZmaWNfbWFuYWdlbWVudBAAchkKFW1vdGl2YXRpb25hbF9jbHVja2luZxAAchMKD2RyaXZlcl90cmFpbmluZxAAchcKE3NoZWxsX2ZvcnRpZmljYXRpb24QAHIUChBlZ2dfbG9hZGluZ19ib3RzEAByDwoLc3VwZXJfYWxsb3kQAHIUChBldmVuX2JpZ2dlcl9lZ2dzEAByFgoSaW50ZXJuYWxfaGF0Y2hlcnk0EAByEwoPcXVhbnR1bV9zdG9yYWdlEAByGAoUZ2VuZXRpY19wdXJpZmljYXRpb24QAHIWChJpbnRlcm5hbF9oYXRjaGVyeTUQAHIRCg10aW1lX2NvbXByZXNzEAByEgoOaG92ZXJfdXBncmFkZXMQAHIUChBncmF2aXRvbl9jb2F0aW5nEAByEAoMZ3Jhdl9wbGF0aW5nEAByEwoPY2hyeXN0YWxfc2hlbGxzEAByFwoTYXV0b25vbW91c192ZWhpY2xlcxAAchIKDm5ldXJhbF9saW5raW5nEAByEwoPdGVsZXBhdGhpY193aWxsEAByGAoUZW5saWdodGVuZWRfY2hpY2tlbnMQAHIUChBkYXJrX2NvbnRhaW5tZW50EAByFwoTYXRvbWljX3B1cmlmaWNhdGlvbhAAchIKDm11bHRpX2xheWVyaW5nEAByFgoSdGltZWxpbmVfZGl2ZXJzaW9uEAByFgoSd29ybWhvbGVfZGFtcGVuaW5nEAByDQoJZWdnc2lzdG9yEAByEgoObWljcm9fY291cGxpbmcQAHIVChFuZXVyYWxfbmV0X3JlZmluZRAAchMKD21hdHRlcl9yZWNvbmZpZxAAchUKEXRpbWVsaW5lX3NwbGljaW5nEAByFAoQaHlwZXJfcG9ydGFsbGluZxAAchsKF3JlbGF0aXZpdHlfb3B0aW1pemF0aW9uEACAAQCQAQCaARA4AUIFCOgHEgBCBQjyBxIAoAEuqAH6AagBAKgBAKgBAA==";
            //var parse1 = new MessageParser<Ei.ContractCoopStatusUpdateRequest>(() => new Ei.ContractCoopStatusUpdateRequest());
            //var res = parse1.ParseFrom(System.Convert.FromBase64String(s));


            var res = new Ei.ContractCoopStatusUpdateRequest {
                ContractIdentifier = ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                Eop = 1, SoulPower = 24, UserId = userid, Amount = 0, Rate = 0, TimeCheatsDetected = 0, PushUserId = userid, BoostTokens = 0, BoostTokensSpent = 0, EggLayingRateBuff = 1, EarningsBuff = 1,
                ProductionParams = new Ei.FarmProductionParams {
                    FarmPopulation = 0, Delivered = 0, Elr = 0, FarmCapacity = 0, Ihr = 0, Sr = 0
                }
            };

            var response = await ContractsAPI.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(res, res.UserId, true);



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
        private static async Task<Ei.CreateCoopResponse> _CreateCoop(string ContractID, Ei.Contract.Types.PlayerGrade grade, Coop coop, double secondsRemaining, string userid, bool subOnly = false) {
            //var request = new Ei.CreateCoopRequest {
            //    ContractIdentifier = ContractID,
            //    CoopIdentifier = coop.Name.ToLower(),
            //    UserId = userid,
            //    Grade = grade,
            //    SecondsRemaining = secondsRemaining
            //};
            var request = new Ei.CreateCoopRequest {
                ContractIdentifier = ContractID,
                CoopIdentifier = coop.Name.ToLower(),
                SecondsRemaining = secondsRemaining,
                UserId = userid,
                UserName = userid,
                Platform = Aux.Platform.Droid,
                ClientVersion = 54,
                SoulPower = 4624103542699216300,
                Eop = 4632655904192331776,
                Grade = grade,
                //Public = false,
                //CcOnly = false,
                //PointsReplay = true,
                AllowAllGrades = true,
            };
            //if(subOnly) {
            //    request.AllowAllGrades = true;
            //    request.CcOnly = true;
            //}
            var response = await ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(request, userid);
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
            if(dbuser.EggIncAccounts.Count > 1) {
                mention += $"({eggIncName})";
            }
            try {
                await targetChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
            } catch(Exception) {
                try {
                    await commandChannel.SendMessageAsync(commandChannel.Guild.Id != targetChannel.Guild.Id ? $"{mention} looks like you are not in the overflow servers. **Make sure and join the overflow servers in <#775558629671698442> to see your co-op, it's in {targetChannel.Guild.Name}**." : "Looks like an error happened, please use /callstaff");
                } catch(Exception) {
                    await targetChannel.SendMessageAsync($"Added {mention}, please join {targetCoop.Name} for the contract {targetCoop.Contract.Name}");
                    return newxref;
                }
            }


            await targetChannel.SendMessageAsync($"Please join {targetCoop.Name} {mention} for the contract {eggEmoji} {targetCoop.Contract.Name}");
            return newxref;
        }
    }
}
