using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static async Task<(List<PotentialCoopGroup> coopGroups, List<(string reason, UserByAccount account)> excluded)> SortUsersIntoDay1Coops(List<DBUser> users, Contract contract, List<Coop> existingCoops, int SkipBG, List<UserCsHistoryEntry> userCsHistoryEntries, Guild dbGuild, SocketGuild guild = null, int overrideNumber = 0) {
            var groups = new List<PotentialCoopGroup>();
            var excluded = new List<(string reason, UserByAccount account)>();

            if(dbGuild is not null && dbGuild.DisableBG) {
                await guild.DownloadUsersAsync();
            }

            //Start compiling a list of all eligible accounts
            var accounts = users
            .SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                Account = a,
                User = u,
                UserCsHistoryEntry = userCsHistoryEntries.Where(x => x.EggIncId == a.Id).MaxBy(x => x.Created),
                //If it's an ultra contract, use UG (UltraGroup), else, use BG (Group)
                Group = a.GetGroup(contract.Details.CcOnly),
                RoleId = dbGuild is not null && dbGuild.DisableBG ? guild.GetUser(u.DiscordId)?.Roles.FirstOrDefault(x => dbGuild.GroupRoles.Contains(x.Id.ToString()))?.Id ?? 0 : 0
            })).ToList();

            FilterAccounts(accounts, excluded, x => x.Account.GetGrade() != Ei.Contract.Types.PlayerGrade.GradeUnset, "Grade is unset");

            FilterAccounts(accounts, excluded, x => x.Account.Backup is not null, "Backup is empty");

            FilterAccounts(accounts, excluded, x => !x.User.TempDisabled, "User disabled");

            FilterAccounts(accounts, excluded, x => x.Account.OnBreakUntil < DateTimeOffset.UtcNow, "On break");

            //If the contract is Subscription only, filter further
            FilterAccounts(accounts, excluded, x => !contract.Details.CcOnly || x.Account.HasActiveSubscription(), "Doesn't have subscription");

            //Need 1k soul eggs for contracts
            FilterAccounts(accounts, excluded, x => x.Account.Backup.SoulEggs >= 1000, "< 1k soul eggs");

            //Need to have the egg unlocked
            FilterAccounts(accounts, excluded, x =>
                x.Account.Backup.MaxEggReached == 0 || (int)x.Account.Backup.MaxEggReached >= (int)contract.Details.Egg || (int)contract.Details.Egg >= 100, "Egg not unlocked");

            FilterAccounts(accounts, excluded, x => !x.Account.Backup.Farms.Any(y => y.ContractId == contract.ID && y.FarmType == Ei.FarmType.Contract), "Already In Co-op");

            FilterAccounts(accounts, excluded, x => !existingCoops.Any(y => y.UserCoopsXrefs.Any(z => z.EggIncId == x.Account.Backup.EggIncId)), "Already assigned a co-op");

            FilterAccounts(accounts, excluded, x => {
                //With no BGs on guilds, filters are disabled - always true
                if(dbGuild is not null && dbGuild.DisableBG) return true;

                // Colleggtible bypass should occur before any possible `return false`-s
                if(UncompleteColleggtibleBypass(x, contract)) return true;

                //If a player does not have a set grade, we can't check the rewards for that grade
                if(x.Account.GetGrade() == Ei.Contract.Types.PlayerGrade.GradeUnset) return false;

                //Try to find the right gradespec, if something goes wrong, default to false
                var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
                if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return false;

                //Figure out which list to use in case of a leggacy
                var leggacyRegisterRewards = new List<Ei.RewardType>();
                if(x.Account.LeggacyAutoRegisterRewards is null || x.Account.LeggacyAutoRegisterRewards.Count == 0) leggacyRegisterRewards = x.Account.AutoRegisterRewards;
                else leggacyRegisterRewards = x.Account.LeggacyAutoRegisterRewards;

                //Which list applies to the current contract?
                var registerRewards = contract.Details.Leggacy ? leggacyRegisterRewards : x.Account.AutoRegisterRewards;
                registerRewards ??= []; //If it's null, initialize it so it has a 0-count

                var completedRewards = x.Account.Backup.Farms.FirstOrDefault(y => y.ContractId == contract.ID)?.NumGoalsAchieved ?? x.Account.Backup.ArchivedFarms.FirstOrDefault(y => y.ContractId == contract.ID)?.NumGoalsAchieved ?? 0;

                //Filter must either be empty, or have at least one reward that matches
                return registerRewards.Count == 0 || registerRewards.Any(r => DBUser.MatchRewards(gradeSpec, r, completedRewards));
            }, "Rewards not selected");

            // Run CheckOnPreviousComplete last so that all other filters are applied to `accounts` first
            // This fixes some issues with  RedoLeggacyOption.YesOtherAccountMatch
            FilterAccounts(accounts, excluded, x => CheckOnPreviousComplete(dbGuild, x, contract, accounts.Where(a => a.User == x.User && a.Account.Id != x.Account.Id).ToList()), "Previously completed");

            
            foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                if(grade == Ei.Contract.Types.PlayerGrade.GradeUnset)
                    continue;
                var includeBg = new List<int>();
                if(dbGuild is not null && dbGuild.DisableBG) {
                    for(var i = 0; i < dbGuild.GroupRoles.Split(",").Length; i++) {
                        var group = new PotentialCoopGroup {
                            Grade = grade,
                            BoardingGroup = i
                        };
                        var roleid = guild.GetRole(ulong.Parse(dbGuild.GroupRoles.Split(",")[i]))?.Id ?? 0;
                        if(roleid == 0) {
                            continue;
                        }
                        groups.Add(group);
                        group.PotentialCoops = _SortUsersIntoDay1Coops(accounts, 0, grade, contract.Details, [], true, AllowGuilds: dbGuild.AllowGuilds, overrideNumber, roleid);
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
                            group.PotentialCoops = _SortUsersIntoDay1Coops(accounts, bg, grade, contract.Details, includeBg, dontMergeDown, AllowGuilds: dbGuild.AllowGuilds, overrideNumber);
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

        private static List<Ei.RewardType> GetRegisterRewards(Guild dbGuild, UserByAccount x, Contract contract) {
            //With no BGs on guilds, filters are disabled - always empty
            if(dbGuild is not null && dbGuild.DisableBG) return null;

            //If a player does not have a set grade, we can't check the rewards for that grade
            if(x.Account.GetGrade() == Ei.Contract.Types.PlayerGrade.GradeUnset) return null;

            //Try to find the right gradespec, if something goes wrong, default to false
            var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
            if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return null;

            //Figure out which list to use in case of a leggacy
            var leggacyRegisterRewards = new List<Ei.RewardType>();
            if(x.Account.LeggacyAutoRegisterRewards is null || x.Account.LeggacyAutoRegisterRewards.Count == 0) leggacyRegisterRewards = x.Account.AutoRegisterRewards;
            else leggacyRegisterRewards = x.Account.LeggacyAutoRegisterRewards;

            //Which list applies to the current contract?
            var registerRewards = contract.Details.Leggacy ? leggacyRegisterRewards : x.Account.AutoRegisterRewards;
            registerRewards ??= []; //If it's null, initialize it so it has a 0-count

            //Filter must either be empty, or have at least one reward that matches
            return registerRewards;
        }

        private static void FilterAccounts(List<UserByAccount> accounts, List<(string, UserByAccount)> excluded, Func<UserByAccount, bool> includeInCoopFilter, string reasonNotIncluded) {
            excluded.AddRange(accounts.Where(x => !includeInCoopFilter(x)).Select(x => (reasonNotIncluded, x)));
            accounts.RemoveAll(x => !includeInCoopFilter(x));
        }

        private static bool MatchGroup(EggIncAccount a1, EggIncAccount a2, Contract c) {
            return(a1.GetGroup(c.cc_only).Equals(a2.GetGroup(c.cc_only)));
        }
        private static bool MatchGrade(EggIncAccount a1, EggIncAccount a2, Contract c) {
            return a1.GetGrade().Equals(a2.GetGrade()) || (c.cc_only && a1.HasActiveSubscription() && a2.HasActiveSubscription());
        }

        private static bool UncompleteColleggtibleBypass(UserByAccount x, Contract contract) {
            if(x.Account.DoUnfinishedCollegtibles && contract.Details.Egg == Ei.Egg.CustomEgg && contract.Details.CustomEggId != "") {
                if(x.Account.Backup.GetColleggtibleLevel(contract.Details.CustomEggId) < 4) return true;
            }
            return false;
        }

        private static bool CheckOnPreviousComplete(Guild dbGuild, UserByAccount x, Contract contract, List<UserByAccount> otherAccounts) {
            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesAll)
                return true;

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesNoUltra && !contract.cc_only)
                return true;

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.YesThreshold && (x.UserCsHistoryEntry?.Cxp ?? 0) <= x.Account.RedoScoreThreshold)
                return true;

            if(otherAccounts.Count > 0 && x.Account.RedoLeggacySelection == RedoLeggacyOption.YesOtherAccountMatch && otherAccounts.Any(ua =>
                ua.Account.Id != x.Account.Id &&
                MatchGrade(ua.Account, x.Account, contract) &&
                MatchGroup(ua.Account, x.Account, contract) &&
                CheckOnPreviousComplete(dbGuild, ua, contract, [])
            )) return true;

            // Colleggtible bypass should occur before any possible falsey returns
            if(UncompleteColleggtibleBypass(x, contract)) return true;

            if(contract.HadTwoRewards && contract.Details.GradeSpecs[((int)x.Account.GetGrade()) - 1].Goals.Count == 3) {
                var completedTwoRewards = (x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2) || x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.NumGoalsAchieved == 2));
                if(completedTwoRewards && !x.Account.DoTwoToThreeContracts) {
                    return false;
                } else if(completedTwoRewards && x.Account.DoTwoToThreeContracts) {
                    // We want to see if the third reward matches filters

                    //Try to find the right gradespec, if something goes wrong, default to false
                    var gradeSpec = contract.Details.GradeSpecs.First(y => y.Grade == x.Account.GetGrade());
                    if(gradeSpec is null || gradeSpec.Grade != x.Account.GetGrade()) return false;

                    //Which list applies to the current contract?
                    var registerRewards = GetRegisterRewards(dbGuild, x, contract);

                    //Null here is not empty list, but an explicit "do not apply"
                    if(registerRewards is null) return false;

                    //Filter must either be empty, or have at least one reward that matches
                    return registerRewards.Count == 0 || registerRewards.Any(r => DBUser.MatchLastReward(gradeSpec, r));
                }
            }

            if(x.Account.RedoLeggacySelection == RedoLeggacyOption.No 
                && (
                    x.Account.Backup.Farms.Any(f => f.Completed && f.ContractId == contract.ID) ||
                    x.Account.Backup.ArchivedFarms.Any(f => f.Completed && f.ContractId == contract.ID)
                ))
                return false;

            return (!x.Account.Backup.Farms.Any(f => f.ContractId == contract.ID && f.Completed) && !x.Account.Backup.ArchivedFarms.Any(f => f.ContractId == contract.ID && f.Completed));
        }

        private static List<PotentialCoop> _SortUsersIntoDay1Coops(IEnumerable<UserByAccount> Accounts, int BoardingGroup, Ei.Contract.Types.PlayerGrade Grade, Ei.Contract contract, List<int> includeBG, bool dontMergeDown, bool AllowGuilds, int overrideNumber = 0, ulong roleid = 0) {
            var matchingAccounts = Accounts.Where(x => x.Account.GetGrade() == Grade || contract.CcOnly );

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
                coops.Add(new PotentialCoop { Users = [], CcOnly = contract.CcOnly });
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
                    coop.Grade = contract.CcOnly ? Ei.Contract.Types.PlayerGrade.GradeAaa : user.Account.GetGrade();
                }

                //Remove user from group so they don't get added to another coop
                highestEBGroup.Value.Remove(user);

                //Look through all groups to find other accounts for this user
                var otherAccounts = ebGroups.SelectMany(x => x.Value.Where(y => y.User.Id == user.User.Id).Select(y => new { Group = x, Account = y })).ToList();
                if(otherAccounts.Count > 0) {
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
                coops = [];
                includeBG.Add(BoardingGroup);
            } else if(includeBG.Count > 0) {
                includeBG.RemoveAll(x => true);
            }
            return coops;
        }
    }
}
