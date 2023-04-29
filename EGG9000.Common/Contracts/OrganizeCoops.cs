using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Contracts {
    public static class OrganizeCoops {
        public static List<PotentialCoopGroup> SortUsersIntoDay1Coops(List<DBUser> users, Ei.Contract contract) {
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

            for(var bg = 1; bg <= 3; bg++) {
                foreach(Ei.Contract.Types.PlayerGrade grade in Enum.GetValues(typeof(Ei.Contract.Types.PlayerGrade))) {
                    var group = new PotentialCoopGroup {
                        BoardingGroup = bg, Grade = grade
                    };
                    groups.Add(group);

                    group.PotentialCoops = SortUsersIntoDay1Coops(accounts, bg, grade, contract);
                }
            }

            return groups;
        }

        private static List<PotentialCoop> SortUsersIntoDay1Coops(IEnumerable<UserByAccount> Accounts, int BoardingGroup, Ei.Contract.Types.PlayerGrade Grade, Ei.Contract contract) {
            var matchingAccounts = Accounts.Where(x => x.Backup.Grade == Grade && x.AccountSettings.Group == BoardingGroup);
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

            return coops;
        }
    }
}
