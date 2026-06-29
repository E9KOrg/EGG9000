using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static async Task<(List<PotentialCoopGroup> coopGroups, List<(string reason, UserByAccount account)> excluded)> SortUsersIntoDay1Coops(List<DBUser> users, Contract contract, List<Coop> existingCoops, int SkipBG, List<UserCsHistoryEntry> userCsHistoryEntries, Guild dbGuild, SeasonInfo contractSeason = null, List<UserSeasonProgress> seasonProgresses = null, SocketGuild guild = null, int overrideNumber = 0) {
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

            // Assignment via the rule engine (Assignment/). Filters `accounts` in place to the keep-set.
            AssignmentEngineFilter.ApplyFilters(accounts, excluded, contract, existingCoops, dbGuild, contractSeason, seasonProgresses);


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

            if(!dontMergeDown && BoardingGroup > 1 && coops.Any(x => (contract.MaxCoopSize - x.Users.Count) > Math.Max(1, contract.MaxCoopSize / 2))) {
                coops = [];
                includeBG.Add(BoardingGroup);
            } else if(includeBG.Count > 0) {
                includeBG.RemoveAll(x => true);
            }
            return coops;
        }

        // Static helper on OrganizeCoops so both the site controller and the bot can load season data without duplicating the query
        public static async Task<(SeasonInfo contractSeason, List<UserSeasonProgress> seasonProgresses)> LoadContractSeasonData(ApplicationDbContext db, Contract contract, List<DBUser> users) {
            if (string.IsNullOrEmpty(contract.SeasonId)) return (null, []);
            var contractSeason = await db.SeasonInfos.FindAsync(contract.SeasonId);
            if (contractSeason == null) return (null, []);
            var eggIncIds = users
                .SelectMany(u => u.EggIncAccounts.Select(a => a.Id))
                .Where(id => id != null)
                .ToList();
            var seasonProgresses = await db.UserSeasonProgresses
                .Where(x => x.SeasonId == contract.SeasonId && eggIncIds.Contains(x.EggIncId))
                .ToListAsync();
            return (contractSeason, seasonProgresses);
        }
    }
}
