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
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static async Task<(List<PotentialCoopGroup> coopGroups, List<(String reason, UserByAccount account)> excluded)> SortUsersIntoDay1Coops(List<DBUser> users, Contract contract, List<Coop> existingCoops, int SkipBG, List<UserCsHistoryEntry> userCsHistoryEntries, Guild dbguild, SocketGuild guild = null, int overrideNumber = 0) {
            var groups = new List<PotentialCoopGroup>();
            var excluded = new List<(String reason, UserByAccount account)>();

            if(dbguild is not null && dbguild.DisableBG) {
                await guild.DownloadUsersAsync();
            }


                var accounts = users
                .SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    Account = a,
                    User = u,
                    UserCsHistoryEntry = userCsHistoryEntries.Where(x => x.EggIncId == a.Id).MaxBy(x => x.Created),
                    //If it's an ultra contract, use UG (UltraGroup), else, use BG (Group)
                    Group = a.GetGroup(contract.Details.CcOnly),
                    RoleId = dbguild is not null && dbguild.DisableBG ? guild.GetUser(u.DiscordId)?.Roles.FirstOrDefault(x => dbguild.GroupRoles.Contains(x.Id.ToString()))?.Id ?? 0 : 0
                }));

            FilterAccounts(accounts, excluded, x => !x.User.TempDisabled, "User disabled");

            FilterAccounts(accounts, excluded, x => x.Account.Backup is not null, "Backup is empty");

            FilterAccounts(accounts, excluded, x => x.Account.OnBreakUntil < DateTimeOffset.Now, "On break");

            FilterAccounts(accounts, excluded, x => CheckOnPreviousComplete(x, contract, accounts.Where(a => a.User == x.User).ToList()), "Previously completed");

            FilterAccounts(accounts, excluded, x => !x.Account.Backup.Farms.Any(y => y.ContractId == contract.ID && y.FarmType == Ei.FarmType.Contract), "Already In Co-op");

            //Need 1k soul eggs for contracts
            FilterAccounts(accounts, excluded, x => x.Account.Backup.SoulEggs >= 1000, "< 1k soul eggs");
            //Need to have the egg unlocked
            FilterAccounts(accounts, excluded, x =>
                x.Account.Backup.MaxEggReached == 0 || (int)x.Account.Backup.MaxEggReached >= (int)contract.Details.Egg || (int)contract.Details.Egg >= 100, "Egg not unlocked");

            //If the contract is Subscription only, filter further
            FilterAccounts(accounts, excluded, x => !contract.Details.CcOnly || x.Account.SubscriptionLevel.HasValue, "Doesn't have subscription");

            FilterAccounts(accounts, excluded, x => !existingCoops.Any(y => y.UserCoopsXrefs.Any(z => z.EggIncId == x.Account.Backup.EggIncId)), "Already assigned a co-op");


            FilterAccounts(accounts, excluded, x => {
                var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
                var registerRewards = contract.Details.Leggacy && x.Account.LeggacyAutoRegisterRewards != null ? x.Account.LeggacyAutoRegisterRewards : x.Account.AutoRegisterRewards;
                var ignoreRewards = dbguild is not null && dbguild.DisableBG;
                return ignoreRewards
                    || registerRewards == null
                    || registerRewards.Count == 0
                    || registerRewards.Any(r => DBUser.MatchRewards(gradeSpec, r));
            }, "Rewards not selected");

            var accountList = accounts.ToList();


            //if(dbguild is not null && dbguild.DisableBG) {
            //    await guild.DownloadUsersAsync();
            //    foreach(var account in accountList) {
            //        var role = guild.GetUser(account.User.DiscordId)?.Roles.FirstOrDefault(x => dbguild.GroupRoles.Contains(x.Id.ToString()));
            //        account.RoleId = role?.Id ?? 0;
            //    }
            //}



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
                        var roleid = guild.GetRole(ulong.Parse(dbguild.GroupRoles.Split(",")[i]))?.Id ?? 0;
                        if(roleid == 0) {
                            continue;
                        }
                        groups.Add(group);
                        group.PotentialCoops = _SortUsersIntoDay1Coops(accountList, 0, grade, contract.Details, new List<int>(), true, AllowGuilds: dbguild.AllowGuilds, overrideNumber, roleid);
                    }
                } else {
                    var bgLimit = contract.Details.CcOnly ? 4 : 3;
                    for(var bg = bgLimit; bg >= 1; bg--) {
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
                            group.PotentialCoops = _SortUsersIntoDay1Coops(accountList, bg, grade, contract.Details, includeBg, dontMergeDown, AllowGuilds: dbguild.AllowGuilds, overrideNumber);
                        }
                    }
                }

                if(contract.Details.CcOnly) {
                    var coops = groups.Where(x => x.PotentialCoops is not null).SelectMany(x => x.PotentialCoops.Select(y => new { BG = x.BoardingGroup, Coop = y })).ToList();

                    groups = coops.GroupBy(x => new { x.BG, x.Coop.Grade }).Select(x => new PotentialCoopGroup {
                        BoardingGroup = x.Key.BG,
                        Grade = x.Key.Grade,
                        PotentialCoops = x.Select(y => y.Coop).ToList()
                    }).ToList();
                    break;
                }
            }



            return (groups, excluded);
        }
        private static IEnumerable<UserByAccount> FilterAccounts(IEnumerable<UserByAccount> accounts, List<(String, UserByAccount)> excluded, Func<UserByAccount, bool> includeInCoopFilter, string reasonNotIncluded) {
            excluded.AddRange(accounts.Where(x => !includeInCoopFilter(x)).Select(x => (reasonNotIncluded, x)));
            return accounts.Where(includeInCoopFilter);
        }

        private static bool CheckOnPreviousComplete(UserByAccount x, Contract contract, List<UserByAccount> userAccounts, bool yesMatchCheck = false) {
            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesAll)
                return true;

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold && (x.UserCsHistoryEntry?.Cxp ?? 0) <= x.Account.RedoScoreThreshold)
                return true;

            if(!yesMatchCheck && x.Account.RedoLeggacySelection == RedoLeggacyOption.YesAccountMatch && userAccounts.Any(ua =>
                ua.Account.Id != x.Account.Id &&
                ua.Account.GetGroup(contract.cc_only).Equals(x.Account.GetGroup(contract.cc_only)) &&
                (ua.Account.GetGrade().Equals(x.Account.GetGrade()) || contract.cc_only && ua.Account.SubscriptionLevel is not null && x.Account.SubscriptionLevel is not null) &&
                CheckOnPreviousComplete(ua, contract, userAccounts, true)
            )) return true;

            if(contract.HadTwoRewards) {
                var completedTwoRewards = (!x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2) || !x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2));
                if(completedTwoRewards) {
                    return false;
                }
            }
            return (!x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.Completed) && !x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.Completed));
        }

        private static List<PotentialCoop> _SortUsersIntoDay1Coops(IEnumerable<UserByAccount> Accounts, int BoardingGroup, Ei.Contract.Types.PlayerGrade Grade, Ei.Contract contract, List<int> includeBG, bool dontMergeDown, bool AllowGuilds, int overrideNumber = 0, ulong roleid = 0) {
            IEnumerable<UserByAccount> matchingAccounts = Accounts.Where(x =>
                    x.Account.GetGrade() == Grade  || contract.CcOnly 
                );

            if(roleid > 0) {
                matchingAccounts = matchingAccounts.Where(x => x.RoleId == roleid);
            } else {
                matchingAccounts = matchingAccounts.Where(x => x.Account.GetGroup(contract.CcOnly) == BoardingGroup || includeBG.Any(y => x.Account.GetGroup(contract.CcOnly) == y));
            }


            matchingAccounts = matchingAccounts.ToList();

            var rng = new Random();
            var ebGroups = matchingAccounts.Shuffle(rng).ToList().GroupBy(x => (int)Math.Log10(x.Account.Backup.EarningsBonus)).ToDictionary(x => x.Key, x => x.ToList());

            var numberOfCoops = Math.Max(Math.Ceiling((double)matchingAccounts.Count() / contract.MaxCoopSize), overrideNumber);

            var coops = new List<PotentialCoop>();
            for(var i = 0; i < numberOfCoops; i++) {
                coops.Add(new PotentialCoop { Users = new List<UserByAccount>(), CcOnly = contract.CcOnly });
            }
            while(ebGroups.Any(x => x.Value.Count > 0)) {
                var coop = coops.OrderBy(x => x.Users.Count).ThenBy(x => x.Users.Sum(u => u.Account.Backup.EarningsBonus)).First();
                var potentionalGuilds = coop.Users.Where(x => !string.IsNullOrWhiteSpace(x.Account.Guild)).Select(x => x.Account.Guild?.Trim()).ToList();
                UserByAccount user;
                KeyValuePair<int, List<UserByAccount>> highestEBGroup;
                if(AllowGuilds) {
                    highestEBGroup = ebGroups.Where(x => x.Value.Count > 0).OrderBy(g => g.Value.Any(u => potentionalGuilds.Any(pc => pc.Equals(u.Account.Guild?.Trim(), StringComparison.InvariantCultureIgnoreCase))) ? 0 : 1).ThenByDescending(x => x.Key).First();
                    user = highestEBGroup.Value.OrderBy(x => potentionalGuilds.Any(pc => pc.Equals(x.Account.Guild?.Trim(), StringComparison.InvariantCultureIgnoreCase)) ? 0 : 1).First();
                } else {
                    highestEBGroup = ebGroups.Where(x => x.Value.Count > 0).OrderByDescending(x => x.Key).First();
                    user = highestEBGroup.Value.First();
                }

                coop.Users.Add(user);
                if(coop.Grade == Ei.Contract.Types.PlayerGrade.GradeUnset) {
                    coop.Grade = user.Account.GetGrade();
                }

                //Remove user from group so they don't get added to another coop
                highestEBGroup.Value.Remove(user);

                //Look through all groups to find other accounts for this user
                var otherAccounts = ebGroups.SelectMany(x => x.Value.Where(y => y.User.Id == user.User.Id).Select(y => new { Group = x, Account = y })).ToList();
                if(otherAccounts.Count() > 0) {
                    //Find out how many other accounts we can add to this coop
                    while(coop.Users.Count + otherAccounts.Count > contract.MaxCoopSize && otherAccounts.Count > 0) {
                        otherAccounts.RemoveAt(otherAccounts.Count - 1);
                    }
                    foreach(var otherAccount in otherAccounts) {
                        coop.Users.Add(otherAccount.Account);

                        //Remove user from group so they don't get added to another coop
                        otherAccount.Group.Value.Remove(otherAccount.Account);
                    }
                }
            }

            //foreach(var ebGroup in ebGroups.OrderByDescending(x => x.Key)) {
            //    foreach(var user in ebGroup.Shuffle(rng)) {
            //        var coop = coops.OrderBy(x => x.Users.Count).ThenBy(x => x.Users.Sum(u => u.Account.Backup.EarningsBonus)).First();
            //        coop.Users.Add(user);
            //    }
            //}

            if(!dontMergeDown && BoardingGroup > 1 && coops.Any(x => (contract.MaxCoopSize - x.Users.Count) > Math.Max(1, contract.MaxCoopSize / 2))) {
                coops = new List<PotentialCoop>();
                includeBG.Add(BoardingGroup);
            } else if(includeBG.Count > 0) {
                includeBG.RemoveAll(x => true);
            }
            return coops;



        }


    }
}
