using EGG9000.Bot;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static List<PotentialCoopGroup> SortUsersIntoDay1Coops(List<DBUser> users, Ei.Contract contract, List<Coop> existingCoops, int SkipBG) {
            var groups = new List<PotentialCoopGroup>();

            var accounts = users
                .Where(x => !x.TempDisabled)
                .SelectMany(u => u.EggIncAccounts.Select(a => new UserByAccount {
                    AccountSettings = a,
                    Backup = u.Backups.FirstOrDefault(b => b.EggIncId == a.Id),
                    User = u
                }));

            accounts = accounts.Where(x => x.AccountSettings.OnBreakUntil < DateTimeOffset.Now && x.Backup is not null);

            accounts = accounts.Where(x =>
                x.AccountSettings.RedoLeggacy ||
                (!x.Backup.Farms.Any(f => f.ContractId == contract.Identifier && f.Completed) && !x.Backup.ArchivedFarms.Any(f => f.ContractId == contract.Identifier && f.Completed))
            );

            accounts = accounts.Where(x => !x.Backup.Farms.Any(y => y.ContractId == contract.Identifier && y.FarmType == Ei.FarmType.Contract));


            accounts = accounts.Where(x => !existingCoops.Any(y => y.UserCoopsXrefs.Any(z => z.EggIncId == x.Backup.EggIncId)));



            foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                if(grade == Ei.Contract.Types.PlayerGrade.GradeUnset)
                    continue;
                var includeBg = new List<int>();
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
                        group.PotentialCoops = _SortUsersIntoDay1Coops(accounts, bg, grade, contract, includeBg, dontMergeDown);
                    }
                }
            }

            return groups;
        }

        private static List<PotentialCoop> _SortUsersIntoDay1Coops(IEnumerable<UserByAccount> Accounts, int BoardingGroup, Ei.Contract.Types.PlayerGrade Grade, Ei.Contract contract, List<int> includeBG, bool dontMergeDown) {
            var matchingAccounts = Accounts.Where(x => 
                x.User.GetGrade(x.Backup.EggIncId) == Grade && 
                (x.AccountSettings.Group == BoardingGroup || includeBG.Any(y => x.AccountSettings.Group == y))
            );
            var gradeSpec = contract.GradeSpecs.First(x => x.Grade == Grade);
            matchingAccounts = matchingAccounts.Where(x =>
                x.AccountSettings.AutoRegisterRewards == null
                || x.AccountSettings.AutoRegisterRewards.Count == 0
                || x.AccountSettings.AutoRegisterRewards.Any(r => DBUser.MatchRewards(gradeSpec, r))
            );

            var ebGroups = matchingAccounts.GroupBy(x => (int)Math.Log10(x.Backup.EarningsBonus));
            var rng = new Random();

            var numberOfCoops = Math.Ceiling((double)matchingAccounts.Count() / contract.MaxCoopSize);
            var coops = new List<PotentialCoop>();
            for(var i = 0; i < numberOfCoops; i++) {
                coops.Add(new PotentialCoop { Users = new List<UserByAccount>() });
            }

            foreach(var ebGroup in ebGroups.OrderByDescending(x => x.Key)) {
                foreach(var user in ebGroup.Shuffle(rng)) {
                    var coop = coops.OrderBy(x => x.Users.Count).ThenBy(x => x.Users.Sum(u => u.Backup.EarningsBonus)).First();
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
