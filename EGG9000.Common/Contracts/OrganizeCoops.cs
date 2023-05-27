using Discord.WebSocket;

using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static async Task<List<PotentialCoopGroup>> SortUsersIntoDay1Coops(List<DBUser> users, Ei.Contract contract, List<Coop> existingCoops, int SkipBG, List<UserCsHistoryEntry> userCsHistoryEntries, Guild dbguild = null, SocketGuild guild = null, int overrideNumber = 0) {
            var groups = new List<PotentialCoopGroup>();

            var accounts = users
                .Where(x => !x.TempDisabled)
                .SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    Account = a,
                    User = u,
                    UserCsHistoryEntry = userCsHistoryEntries.Where(x => x.EggIncId == a.Id).MaxBy(x => x.Created)
                }));
            accounts = accounts.Where(x => x.Account.OnBreakUntil < DateTimeOffset.Now && x.Account.Backup is not null);

            accounts = accounts.Where(x => CheckOnPreviousComplete(x, contract)
            );

            accounts = accounts.Where(x => !x.Account.Backup.Farms.Any(y => y.ContractId == contract.Identifier && y.FarmType == Ei.FarmType.Contract));

            //Need 1k soul eggs for contracts
            accounts = accounts.Where(x => x.Account.Backup.SoulEggs >= 1000);
            //Need to have the egg unlocked
            accounts = accounts.Where(x => (int)x.Account.Backup.MaxEggReached >= (int)contract.Egg);

            accounts = accounts.Where(x => !existingCoops.Any(y => y.UserCoopsXrefs.Any(z => z.EggIncId == x.Account.Backup.EggIncId)));


            var accountList = accounts.ToList();

            if(dbguild is not null && dbguild.DisableBG) {
                await guild.DownloadUsersAsync();
                foreach(var account in accountList) {
                    var role = guild.GetUser(account.User.DiscordId)?.Roles.FirstOrDefault(x => dbguild.GroupRoles.Contains(x.Id.ToString()));
                    account.RoleId = role?.Id ?? 0;
                }
            }


            foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                if(grade == Ei.Contract.Types.PlayerGrade.GradeUnset)
                    continue;
                var includeBg = new List<int>();
                if(dbguild is not null && dbguild.DisableBG) {
                    for(var i = 0; i < dbguild.GroupRoles.Split(",").Count(); i++) {
                        var group = new PotentialCoopGroup {
                            Grade = grade,
                            BoardingGroup = i
                        };
                        groups.Add(group);
                        var roleid = guild.GetRole(ulong.Parse(dbguild.GroupRoles.Split(",")[i])).Id;

                        group.PotentialCoops = _SortUsersIntoDay1Coops(accountList, 0, grade, contract, new List<int>(), true, true, overrideNumber, roleid);
                    }
                } else {
                    for(var bg = 3; bg >= 1; bg--) {
                        var group = new PotentialCoopGroup {
                            BoardingGroup = bg, Grade = grade
                        };
                        groups.Add(group);

                        var dontMergeDown = false;
                        if(SkipBG > 0 && SkipBG == bg - 1) {
                            for(var i = SkipBG; i > 0; i--) {
                                includeBg.Add(i);
                            }
                            dontMergeDown = true;
                        }

                        if(bg > SkipBG) {
                            group.PotentialCoops = _SortUsersIntoDay1Coops(accountList, bg, grade, contract, includeBg, dontMergeDown, false);
                        }
                    }
                }
            }

            return groups;
        }

        private static bool CheckOnPreviousComplete(UserByAccount x, Ei.Contract contract) {
            return x.Account.RedoLeggacySelection == RedoLeggacyOption.YesAll ||
                (x.Account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold && (x.UserCsHistoryEntry?.Cxp ?? 0) <= x.Account.RedoScoreThreshold) ||
                (!x.Account.Backup.Farms.Any(f => f.ContractId == contract.Identifier && f.Completed) && !x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.Identifier && f.Completed));
        }

        private static List<PotentialCoop> _SortUsersIntoDay1Coops(IEnumerable<UserByAccount> Accounts, int BoardingGroup, Ei.Contract.Types.PlayerGrade Grade, Ei.Contract contract, List<int> includeBG, bool dontMergeDown, bool ignoreRewards, int overrideNumber = 0,  ulong roleid = 0) {
            IEnumerable<UserByAccount> matchingAccounts;

            if(roleid > 0) {
                matchingAccounts = Accounts.Where(x =>
                    x.Account.GetGrade() == Grade &&
                    x.RoleId == roleid
                );
            } else {
                matchingAccounts = Accounts.Where(x =>
                    x.Account.GetGrade() == Grade &&
                    (x.Account.Group == BoardingGroup || includeBG.Any(y => x.Account.Group == y))
                );
            }
            var gradeSpec = contract.GradeSpecs.First(x => x.Grade == Grade);
            matchingAccounts = matchingAccounts.Where(x =>
                   ignoreRewards 
                || x.Account.AutoRegisterRewards == null
                || x.Account.AutoRegisterRewards.Count == 0
                || x.Account.AutoRegisterRewards.Any(r => DBUser.MatchRewards(gradeSpec, r))
            );
            matchingAccounts = matchingAccounts.ToList();

            var ebGroups = matchingAccounts.GroupBy(x => (int)Math.Log10(x.Account.Backup.EarningsBonus));
            var rng = new Random();

            var numberOfCoops = Math.Max(Math.Ceiling((double)matchingAccounts.Count() / contract.MaxCoopSize), overrideNumber);
            
            var coops = new List<PotentialCoop>();
            for(var i = 0; i < numberOfCoops; i++) {
                coops.Add(new PotentialCoop { Users = new List<UserByAccount>() });
            }

            foreach(var ebGroup in ebGroups.OrderByDescending(x => x.Key)) {
                foreach(var user in ebGroup.Shuffle(rng)) {
                    var coop = coops.OrderBy(x => x.Users.Count).ThenBy(x => x.Users.Sum(u => u.Account.Backup.EarningsBonus)).First();
                    coop.Users.Add(user);
                }
            }

            if(!dontMergeDown && BoardingGroup > 1 && coops.Any(x => (contract.MaxCoopSize - x.Users.Count) > 1)) {
                coops = new List<PotentialCoop>();
                includeBG.Add(BoardingGroup);
            } else if(includeBG.Count > 0) {
                includeBG.RemoveAll(x => true);
            }
            return coops;
        }


    }
}
