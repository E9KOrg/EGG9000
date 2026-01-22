using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Humanizer;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using static Ei.ContractCoopStatusResponse.Types;

namespace EGG9000.Common.Helpers {
    public static class Prefarm {
        public class UserWithBackup {
            public DBUser User { get; set; }
            public CustomBackup Backup { get; set; }
            public EggIncAccount Account { get; set; }

        }
        public class LeaderboardUser {
            public DBUser User { get; set; }
            public EggIncAccount Account {
                get {
                    return User.EggIncAccounts.First(x => x.Id == Backup.EggIncId);
                }
            }
            public CustomBackup Backup { get; set; }
            public DateTimeOffset? lastSeen { get; set; }
            //public int RecentContracts { get; set; }
            //public int ActiveContracts { get; set; }
            public int TotalContracts { get; set; }
            public bool Last1 { get; set; }
            public bool Last2 { get; set; }
            public bool Last3 { get; set; }
            public bool Last4 { get; set; }
            public bool Last5 { get; set; }
            //public string Role { get; set; }
            public SocketGuildUser DiscordUser { get; set; }
            public bool Elite { get { return Backup.EarningsBonus > 10000000000000; } }
            public DateTimeOffset Started { get; set; }
            //public bool Active { get; set; }
            public List<SimpleXref> RecentXrefs { get; set; }
            public double TotalCS { get; set; }
            public double SeasonCS { get; set; }

            public double TotalCraftingXP { get; set; }
            public uint CraftingLevel { get; set; }
        }

        public class SimpleXref {
            //public bool LastThreeWeeks { get; set; }
            public Guid UserId { get; set; }
            public string ContractID { get; set; }
            public string EggIncId { get; set; }
            public bool Joined { get; set; }
        }

        public class UserPreFarm {
            public DateTimeOffset? Started { get; set; }
            public string EggIncId { get; set; }
            public ulong DiscordId { get; set; }
            public Guid? DatabaseId { get; set; }
            public SocketGuildUser DiscordUser { get; set; }
            public string Name { get; set; }
            public double EggsPaidFor { get; set; }
            public double NumChickens { get; set; }
            public double Rate { get; set; }
            public double Projected { get; set; }
            public double ProjectedPercent {
                get {
                    return Projected / Goal * 100;
                }
            }
            public double Goal { get; set; }
            public double OfflineEggs { get; set; }
            public ushort Tokens { get; set; }
            public ushort BoostTokensSpent { get; set; }
            public TimeSpan TimeSinceUpdate { get; set; }
            public string Coop { get; set; }
            public string CoopName { get; set; }
            public TimeSpan? TimeLeft { get; set; }
            public bool CancelledFarm { get; set; }

            public DBUser User { get; set; }
            public uint League { get; set; }
            public bool Completed { get; set; } = false;

            public CustomBackup Backup { get; set; }

            public bool PotentialBoxCarry { get; set; }
            public UserCoopXref Xref { get; set; }
            public Ei.ContractCoopStatusResponse.Types.ContributionInfo ContributionInfo { get; set; }
        }

        public static string GetTimeRemaining(double targetAmount, double currentRate, double currentAmount) {
            var remainingAmount = targetAmount - currentAmount;
            var remainingSeconds = remainingAmount / currentRate;
            if(remainingSeconds >= TimeSpan.MaxValue.TotalSeconds) {
                return "∞";
            }
            if(remainingSeconds <= TimeSpan.MinValue.TotalSeconds) {
                return "-∞";
            }

            return DiscordHelpers.TimeStamper(double.IsNaN(remainingSeconds) ? TimeSpan.MaxValue : TimeSpan.FromSeconds(remainingSeconds), DiscordHelpers.DiscordTimestampFormat.Relative);
            //return TimeSpan.FromSeconds(remainingSeconds).Humanize(precision: 2).ShortenTime();
        }

        public static TimeSpan GetTimeRemainingValue(double targetAmount, double currentRate, double currentAmount) {
            var remainingAmount = targetAmount - currentAmount;
            var remainingSeconds = remainingAmount / currentRate;
            if(remainingSeconds >= TimeSpan.MaxValue.TotalSeconds) {
                return TimeSpan.MaxValue;
            }
            if(remainingSeconds <= TimeSpan.MinValue.TotalSeconds) {
                return TimeSpan.MinValue;
            }
            return double.IsNaN(remainingSeconds) ? TimeSpan.MaxValue : TimeSpan.FromSeconds(remainingSeconds);
        }

        public static TimeSpan GetTimeRemainingValue(double targetAmount, List<UserPreFarm> userPreFarms) {
            return GetTimeRemainingValue(targetAmount, userPreFarms.Sum(x => x.Rate), userPreFarms.Sum(x => x.EggsPaidFor + x.OfflineEggs));
        }

        public static TimeSpan GetTimeRemainingValue(double targetAmount, List<UserFarmDetails> coopParticipants) {
            return GetTimeRemainingValue(targetAmount, coopParticipants.Sum(x => x.Rate), coopParticipants.Sum(x => x.EggsShipped + x.OfflineEggs));
        }

        public class CoopsBreakdown {
            public List<CoopDetails> ExistingCoops { get; set; }
            public List<CoopDetails> PotentialCoops { get; set; }
            public List<UserFarmDetails> AlreadyInCoop { get; set; }
            public List<UserFarmDetails> Completed { get; set; }
            public List<UserFarmDetails> ExpiredFarms { get; set; }
            //public List<CoopParticipant> AllPreFarms { get; set; }
        }

        public class CoopDetails {
            public List<UserFarmDetails> CoopParticipants { get; private set; }
            public Coop Coop { get; set; }
            public double PercentProjected { get; private set; }
            public double PercentProjectedForJoined { get; private set; }
            public double Projected { get; private set; }
            public TimeSpan TimeRemaining { get; private set; }

            private uint _maxSize = 0;
            public bool HasSpots { get { return CoopParticipants.Count < _maxSize; } }
            public bool IsFire;
            public bool IsDoubleFire;
            public double TargetAmount { get; set; }
            public CoopDetails(Coop coop, Contract contract, uint league, IList<UserWithBackup> backups, List<DBCustomEgg> customEggs, DiscordSocketClient discord, Ei.ContractCoopStatusResponse status = null) {
                var coopParticipants = GetCoopParticipants(coop, contract, league, status ?? coop.LastStatusUpdate, backups, customEggs, discord);
                Coop = coop;
                SetCoopDetails(coopParticipants, contract, league);
            }
            public CoopDetails(List<UserFarmDetails> coopParticipants, GuildContract guildContract, uint league) {
                SetCoopDetails(coopParticipants, guildContract.Contract, league);
            }
            public void SetCoopDetails(List<UserFarmDetails> coopParticipants, Contract contract, uint league) {
                CoopParticipants = coopParticipants.Where(x => x.DBUser is not null || x.CoopStatus is not null).ToList();
                TargetAmount = contract.Details.GetGoals((int)league).Last().TargetAmount;
                if(TargetAmount > 0) {
                    TimeRemaining = GetTimeRemainingValue(TargetAmount, CoopParticipants);
                    Projected = CoopParticipants.Sum(x => x.Projected);
                    PercentProjected = (Projected / TargetAmount) * 100;
                    PercentProjectedForJoined = CoopParticipants.Where(x => x.CoopStatus is not null).Sum(x => x.Projected) / TargetAmount * 100;
                    if(TimeRemaining < TimeSpan.FromHours(18)) {
                        IsDoubleFire = true;
                    } else if(TimeRemaining < TimeSpan.FromHours(36)) {
                        IsFire = true;
                    }

                }
                _maxSize = (uint)contract.MaxUsers;
            }


            public double GetProjectedShare(UserFarmDetails ufd) {
                var share = (ufd.EggsShipped + ufd.OfflineEggs + ufd.Rate * Math.Max(0, TimeRemaining.TotalSeconds)) / TargetAmount * 100;
                if(share > 99)
                    share = Math.Floor(share);
                return Math.Max(0, share);
            }
        }

        public static async Task<CoopsBreakdown> GetBreakdown(ApplicationDbContext db, GuildContract guildContract, DiscordSocketClient discord, uint league) {
            var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildContract.GuildID).ToListAsync();
            var backups = dbusers.Where(x => x.GuildId == guildContract.GuildID).SelectMany(y => y.EggIncAccounts.Where(x => x.Backup is not null).Select(x => new UserWithBackup {
                User = y,
                Backup = x.Backup,
                Account = x
            })).ToList();

            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == league).ToListAsync();


            var missingXrefUsers = coops.SelectMany(c => c.UserCoopsXrefs.Where(x => !backups.Any(b => b.User.Id == x.UserId))).Select(x => x.UserId);

            var missingUsers = await db.DBUsers.Where(x => missingXrefUsers.Contains(x.Id)).ToListAsync();
            backups.AddRange(missingUsers.SelectMany(u => u.EggIncAccounts.Where(x => x.Backup is not null).Select(b => new UserWithBackup { User = u, Backup = b.Backup })));

            var customEggs = await db.GetCustomEggsAsync();

            var coopsBreakdown = GetBreakdown(coops, backups, guildContract, customEggs, discord, league);
            return coopsBreakdown;
        }

        public static CoopsBreakdown GetBreakdown(List<Coop> coops, List<UserWithBackup> usersWithBackups, GuildContract guildContract, List<DBCustomEgg> customEggs, DiscordSocketClient discord, uint league) {
            coops = coops.Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6)).ToList();

            var coopsBreakdown = new CoopsBreakdown {
                ExistingCoops = coops.Select(c => new CoopDetails(c, guildContract.Contract, league, usersWithBackups, customEggs, discord, c.LastStatusUpdate)).ToList()
            };

            var notAssignedCoop = usersWithBackups
                .Where(x =>
                    !coopsBreakdown.ExistingCoops.Any(c => c.CoopParticipants.Any(p => (p.Xref?.EggIncId) == x.Backup.EggIncId))
                )
                .Select(x => new UserFarmDetails(guildContract.Contract, x, customEggs, discord, league));

            notAssignedCoop = notAssignedCoop.Where(x => x.Backup != null && !x.DBUser.TempDisabled && x.DBUser.GuildId == guildContract.GuildID &&
                    (
                        x.Backup.Farms.Any(y => y.ContractId == guildContract.ContractID && (y.Completed || guildContract.League == y.League)) ||
                        (x.Backup.ArchivedFarms?.Any(f => f.ContractId == guildContract.ContractID && (f.Completed || guildContract.League == f.League)) ?? false)
                    )
                )
                .OrderByDescending(x => x.Projected);

            notAssignedCoop = notAssignedCoop.Where(x => (x.League == guildContract.League || x.Completed) && (x.Farm is not null || x.ArchivedFarm is not null));

            var completed = notAssignedCoop.Where(x => x.Completed).OrderBy(x => x.Name).ToList();
            var currentUsers = notAssignedCoop.Where(x => !x.Completed && !x.CancelledFarm).ToList();

            var alreadyInCoop = currentUsers.Where(x => x.InCoop).OrderBy(x => x.Name).ToList();
            var notInCoop = currentUsers.Where(x => !x.InCoop && x.NumChickens > 500).ToList();

            coopsBreakdown.AlreadyInCoop = alreadyInCoop;
            coopsBreakdown.Completed = completed;

            coopsBreakdown.ExpiredFarms = notInCoop.Where(x => x.TimeLeft.TotalSeconds <= 0).ToList();
            notInCoop.RemoveAll(x => coopsBreakdown.ExpiredFarms.Any(expired => expired.EggIncId == x.EggIncId));


            var numPerCoop = Math.Max(guildContract.Contract.Details.MaxCoopSize, 1);
            var numOfCoops = Math.Ceiling((decimal)notInCoop.Count / numPerCoop);
            var size = guildContract.NumberOfCoops;

            if(size > 0) {
                numOfCoops = Math.Max(size, numOfCoops);
            }


            var potentialCoops = new List<List<UserFarmDetails>>();


            for(var i = 1; i <= numOfCoops; i++) {
                potentialCoops.Add([]);
            }

            if(numPerCoop >= 4) {
                var groupedAccounts = notInCoop.GroupBy(x => x.DBUser.Id).Where(x => x.Count() > 1);
                foreach(var groupedAccount in groupedAccounts) {
                    var accounts = groupedAccount.ToList();
                    var allowedAccounts = numPerCoop / 4 + 1;
                    if(groupedAccount.Count() > allowedAccounts) {
                        accounts = groupedAccount.OrderBy(x => x.Backup.EarningsBonus).Take((int)allowedAccounts).ToList();
                    }
                    if(accounts.Count > 1) {
                        var smallestCoop = potentialCoops.Where(x => x.Count < numPerCoop - accounts.Count).OrderBy(x => x.Sum(y => y.Projected)).First();
                        foreach(var account in accounts) {
                            smallestCoop.Add(account);
                            notInCoop.Remove(account);
                        }

                    }
                }
            }

            var targetAmount = guildContract.Contract.Details.GetGoals((int)league).Last().TargetAmount;



            var notInCoopAbove5Percent = notInCoop.Where(x => x.ProjectedPercent >= 5).ToList();
            var notInCoopBelow5Percent = notInCoop.Where(x => x.ProjectedPercent < 5).OrderByDescending(x => x.Backup.EarningsBonus).ToList();


            foreach(var u in notInCoopAbove5Percent) {
                //List<UserFarmDetails> smallestCoop;
                var smallestCoop = potentialCoops.Where(x => x.Count < numPerCoop).OrderBy(x => x.Sum(y => y.Projected)).First();
                smallestCoop.Add(u);
            }

            foreach(var u in notInCoopBelow5Percent) {
                var lowestEBCarryCoop = potentialCoops.Where(x => x.Count < numPerCoop).OrderBy(x => x.Select(y => y.Backup.EarningsBonus).DefaultIfEmpty(0).Max()).First();
                if(u.EarningsBonus > lowestEBCarryCoop.Select(x => x.Backup.EarningsBonus).DefaultIfEmpty(0).Max()) {
                    lowestEBCarryCoop.Add(u);
                } else if(potentialCoops.Any(x => x.Count < numPerCoop / 2)) {
                    var smallestCoops = potentialCoops.Where(x => x.Count < numPerCoop).OrderBy(x => x.Count).First();
                    smallestCoops.Add(u);
                } else {
                    var lowestEBRatingCoop = potentialCoops.Where(x => x.Count < numPerCoop).MinBy(x => x.Sum(y => Math.Log10(y.Backup.EarningsBonus)));
                    lowestEBRatingCoop.Add(u);
                }
            }

            coopsBreakdown.PotentialCoops = [];
            for(var i = 1; i <= potentialCoops.Count; i++) {
                var details = new CoopDetails(potentialCoops[i - 1], guildContract, league);
                coopsBreakdown.PotentialCoops.Add(details);
            }

            coopsBreakdown.PotentialCoops = [.. coopsBreakdown.PotentialCoops.OrderByDescending(x => x.PercentProjected)];
            //coopsBreakdown.AllPreFarms = coopParticipants;
            return coopsBreakdown;
        }

        public static async Task<List<UserPreFarm>> GetBackupsForAliens(List<Coop> coops, List<UserPreFarm> allPrefarms, List<DBCustomEgg> customEggs, Contract contract, APILink apiLink) {
            var tasks = new List<Task<CustomBackup>>();
            foreach(var coop in coops) {
                if(coop.LastStatusUpdate != null) {
                    foreach(var c in coop.LastStatusUpdate.Contributors) {
                        var prefarm = allPrefarms.FirstOrDefault(x => x.EggIncId == c.UserId);
                        if(prefarm == null) {
                            tasks.Add(ContractsAPI.GetBackupAsync(c.UserId));

                        }
                    }
                }
            }
            await Task.WhenAll(tasks);
            var alienPrefarms = new List<UserPreFarm>();
            foreach(var task in tasks) {
                var lUser = new LeaderboardUser { Backup = task.Result };
                if(lUser.Backup != null) {
                    alienPrefarms.Add(BackupToPreFarm(lUser, contract, customEggs));
                }
            }
            return alienPrefarms;
        }

        public static List<UserPreFarm> GetPrefarmsForCoop(Coop coop, List<UserPreFarm> allPrefarms, List<UserPreFarm> alienPrefarms) {
            var prefarms = new List<UserPreFarm>();

            if(coop.LastStatusUpdate != null) {
                foreach(var c in coop.LastStatusUpdate.Contributors) {
                    var prefarm = allPrefarms.FirstOrDefault(x => x.EggIncId == c.UserId);
                    prefarm ??= alienPrefarms.FirstOrDefault(x => x?.EggIncId == c.UserId);
                    if(prefarm != null)
                        prefarms.Add(prefarm);
                }
            }

            foreach(var prefarm in allPrefarms) {
                var xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.EggIncId == prefarm.EggIncId);
                if(xref is not null)
                    prefarm.Xref = xref;
            }

            foreach(var xref in coop.UserCoopsXrefs) {
                if(!prefarms.Any(x => x.EggIncId == xref.EggIncId || x.EggIncId == xref.RefEggIncId)) {
                    var prefarm = allPrefarms.FirstOrDefault(x => x.EggIncId == xref.EggIncId || x.EggIncId == xref.RefEggIncId);
                    if(prefarm == null) {
                        if(xref.User?.GuildId == coop.GuildId) {
                            prefarm = new UserPreFarm {
                                Name = xref.User.DiscordUsername
                            };
                        } else {
                            continue;
                        }
                    }
                    prefarms.Add(prefarm);
                }
            }


            prefarms.ForEach(prefarm => {
                if(!string.IsNullOrWhiteSpace(prefarm.Coop) && prefarm.Coop.ToLower() != coop.Name.ToLower() && !prefarm.CancelledFarm && !prefarm.Coop.StartsWith("✔️") && !prefarm.Coop.StartsWith("❌") && !prefarm.Coop.Contains("Different")) {
                    //x.Coop += " (Different Coop)";
                } else {
                    ContributionInfo contribution = null;
                    if(coop.LastStatusUpdate is not null) {
                        var contributors = coop.LastStatusUpdate.Contributors;
                        contribution = contributors.FirstOrDefault(x => x.UserId == prefarm.EggIncId);
                        if(contribution is null && prefarm.Xref is not null) {
                            contribution = contributors.FirstOrDefault(x => x.UserName == prefarm?.Xref.FixedUserName);
                        }
                    }

                    if(contribution is not null) {
                        prefarm.Rate = contribution.ContributionRate;
                        prefarm.Projected = contribution.Projected;
                    }

                    var joined = contribution is not null;


                    if(coop.Status == CoopStatusEnum.Failed && string.IsNullOrEmpty(prefarm.CoopName)) {
                        prefarm.Coop = "";
                    } else {
                        prefarm.Coop = joined ? "✔️" : $"❌{prefarm.TimeLeft?.Humanize(precision: 2).ShortenTime().Replace(" ", "").Replace(",", "")}";
                        if(prefarm.Name.StartsWith("*")) {
                            prefarm.Coop = "👽";
                            prefarm.Name = prefarm.Name[1..];
                        }
                    }
                }

            });

            return prefarms;
        }

        public static List<UserFarmDetails> GetCoopParticipants(Coop coop, Contract contract, uint league, Ei.ContractCoopStatusResponse status, IEnumerable<UserWithBackup> backups, List<DBCustomEgg> customEggs, DiscordSocketClient discord) {
            var coopParticipants = new List<UserFarmDetails>();


            var userBackupsAssigned = backups.Where(x => coop.UserCoopsXrefs.Any(y => y.UserId == x.User.Id)).ToList();

            //Add joined participants
            if(status is not null) {
                foreach(var participant in status.Participants) {

                    //Try and match UUID
                    var backup = backups.Where(x => x.Backup is not null).FirstOrDefault(x => x.Backup.Farms.Any(farm => farm.ReportedUUIDs is not null && farm.ReportedUUIDs.Any(uuid => uuid == participant.Uuid)));
                    //UserWithBackup backup = null;
                    if(backup is not null) {
                        var thisXref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == backup.User.Id && x.EggIncId == backup.Backup.EggIncId);
                        coopParticipants.Add(new UserFarmDetails(coop, thisXref, participant, contract, backup, customEggs, discord, league));
                    } else {
                        if(participant.UserName == "[departed]") continue;
                        UserCoopXref xref = null;
                        if(coop is not null) {

                            xref = coop.UserCoopsXrefs.FirstOrDefault(xref => xref.EggIncId == participant.GetID());
                            var saveFixedUserName = false;

                            //First try for FixedUserName
                            xref ??= coop.UserCoopsXrefs.FirstOrDefault(x => !string.IsNullOrEmpty(x.FixedUserName) && x.FixedUserName == participant.UserName);

                            if(xref == null) {
                                //Try matching username to assigned backup
                                var matchbackup = userBackupsAssigned.FirstOrDefault(x => x.Backup.UserName == participant.UserName);
                                xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                            }

                            if(xref == null) {
                                //Try matching to EB
                                var matchbackup = userBackupsAssigned.FirstOrDefault(x => Math.Log10(x.Backup.EarningsBonus / 100) == participant.SoulPower);
                                xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                                if(xref is not null) saveFixedUserName = true;
                            }

                            if(xref == null) {
                                //Try matching to Soul Eggs
                                var matchbackup = userBackupsAssigned.FirstOrDefault(x => x.Backup.SoulEggs == participant.FarmInfo?.SoulEggs);
                                xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                                if(xref is not null) saveFixedUserName = true;
                            }

                            if(xref == null && !string.IsNullOrWhiteSpace(participant.UserName)) {
                                //Now try to match a backup username
                                var matchbackup = backups.FirstOrDefault(x => x.Backup?.UserName == participant.UserName);
                                xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                                if(xref is not null) saveFixedUserName = true;
                            }


                            if(saveFixedUserName) {
                                var isNameUnique = !backups.Any(b => b.Backup?.UserName == participant.UserName && b.Backup?.EggIncId != xref.EggIncId);
                                var matchingBackup = backups.First(b => b.User.Id == xref.UserId);
                                var isInCoop = matchingBackup.Backup.Farms.Any(f => f.CoopId is not null && f.CoopId.Equals(coop.Name, StringComparison.InvariantCultureIgnoreCase));
                                if(isNameUnique && !string.IsNullOrEmpty(participant.UserName) && isInCoop)
                                    xref.FixedUserName = participant.UserName;
                            }
                        }
                        backup = xref is not null ? backups.OrderByDescending(x => x.User.GuildId != 0).FirstOrDefault(b => b.Backup?.EggIncId == xref.EggIncId) : null;
                        coopParticipants.Add(new UserFarmDetails(coop, xref, participant, contract, backup, customEggs, discord, league));
                    }
                }
            }


            foreach(var missingUser in coopParticipants.Where(x => x.Xref is null && x.CoopStatus is not null && x.Backup is null)) {
                if(missingUser.CoopStatus.UserName == "[departed]") continue;
                var user = backups.Where(x => x.Backup is not null).FirstOrDefault(x => missingUser.CoopStatus.UserName.Length > 0 && x.Backup.UserName == missingUser.CoopStatus.UserName && x.Backup.Farms.Any(y => y.ContractId == contract.ID));
                if(user is not null) {
                    missingUser.DBUser = user.User;
                    missingUser.Backup = user.Backup;
                    if(missingUser.DBUser.EggIncAccounts.Count == 1 && coop.UserCoopsXrefs.Any(x => x.UserId == missingUser.DBUser.Id)) {
                        missingUser.AddXref(coop.UserCoopsXrefs.First(x => x.UserId == missingUser.DBUser.Id));
                    }
                } else {
                    var matchingBackups = backups.Where(x => x.Backup is not null).Where(x => missingUser.CoopStatus.UserName.Length > 0 && x.User is not null && !string.IsNullOrEmpty(x.User?.Usernames) && (x.User.Usernames.Contains(missingUser.CoopStatus.UserName))).ToList();
                    if(matchingBackups is not null && matchingBackups.Count > 0) {
                        if(matchingBackups.Count > 1) {
                            var index = matchingBackups.First().User.Usernames.Split(",").ToList().IndexOf(missingUser.CoopStatus.UserName);
                            if(index > -1 && matchingBackups.Count >= index + 1) {
                                missingUser.DBUser = matchingBackups.First().User;
                                missingUser.Backup = matchingBackups[index].Backup;
                                if(missingUser.DBUser.EggIncAccounts.Count == 1 && coop.UserCoopsXrefs.Any(x => x.UserId == missingUser.DBUser.Id)) {
                                    missingUser.AddXref(coop.UserCoopsXrefs.First(x => x.UserId == missingUser.DBUser.Id));
                                }
                            }
                        } else {
                            missingUser.DBUser = matchingBackups.First().User;
                            missingUser.Backup = matchingBackups.First().Backup;
                            if(missingUser.DBUser.EggIncAccounts.Count == 1 && coop.UserCoopsXrefs.Any(x => x.UserId == missingUser.DBUser.Id)) {
                                missingUser.AddXref(coop.UserCoopsXrefs.First(x => x.UserId == missingUser.DBUser.Id));
                            }
                        }
                    }
                }
            }

            if(coop is not null) {
                //Add missing participants
                var missingXrefs = coop.UserCoopsXrefs.Where(x => !coopParticipants.Any(y => y is not null && y.Xref == x));

                foreach(var xref in missingXrefs) {
                    var backup = backups.FirstOrDefault(b => b.Backup?.EggIncId == xref.EggIncId);
                    backup ??= backups.FirstOrDefault(b => b.User.Id == xref.UserId);
                    backup ??= xref.User?.EggIncAccounts.Select(b => new UserWithBackup { User = xref.User, Backup = b.Backup }).FirstOrDefault();
                    backup ??= backups.FirstOrDefault(b => b.User.Id == xref.UserId);
                    if(backup is not null) {
                        var participant = new UserFarmDetails(coop, xref, contract, backup, customEggs, discord, league);
                        coopParticipants.Add(participant);
                    }
                }
            }


            if(coopParticipants.Any(x => x.Name.ToLower() == "kendrome" && !coop.FinishedOrFailedOrExpired())) {

            }
            return coopParticipants;
        }

        public static UserPreFarm BackupToPreFarm(LeaderboardUser user, Contract contract, List<DBCustomEgg> customEggs) {
            var farm = user.Backup.Farms?.FirstOrDefault(x => x.ContractId == contract.ID);
            if(farm == null) {
                if(!user.Backup.EmptyBackup && (user.Backup.ArchivedFarms?.Any(f => f.ContractId == contract.ID) ?? false)) {
                    return new UserPreFarm {
                        EggIncId = user.Backup.EggIncId,
                        DatabaseId = user.User?.Id,
                        DiscordId = user.User?.DiscordId ?? 0,
                        Name = user.User?.DiscordUsername ?? "*" + user.Backup.UserName,
                        User = user.User,
                        Backup = user.Backup,
                        Completed = true,
                    };
                }
                return null;
            }
            var farmStats = farm?.WithStats(user.Backup, null, customEggs);

            var prefarm = new UserPreFarm {
                EggIncId = user.Backup.EggIncId,
                DatabaseId = user.User?.Id,
                DiscordId = user.User?.DiscordId ?? 0,
                Name = user.User?.DiscordUsername ?? "*" + user.Backup.UserName,
                League = farm.League ?? 0,
                User = user.User,
                Coop = farm.CoopId,
                CoopName = farm.CoopId,
                CancelledFarm = farm.Cancelled,
                Backup = user.Backup,
                Completed = farm.Completed,
                EggsPaidFor = farm.EggsPaidFor
            };

            var goal = contract.Details.GetGoals(user.Elite ? 0 : 1).Last().TargetAmount;

            //var farmDetails = user.Backup.GetFarmDetails(farm);

            prefarm.CancelledFarm = prefarm.CancelledFarm || farm.NumChickens == 0;
            var ratePerSec = farmStats.CurrentShippingRate;// user.Backup.get Research.GetEggShippedRatePerSec(farm, user.Backup.Game.EpicResearch.ToList());


            var siloTimeMinutes = user.Backup != null && farm != null ? (Research.GetTotalSiloCapacity(user.Backup) * farm.SilosOwned) : 0;
            var contractLength = contract.Details.LengthSeconds;
            var TimeSinceUpdate = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds((long)farm.LastStepTime);
            if(TimeSinceUpdate.TotalMinutes > siloTimeMinutes) {
                TimeSinceUpdate = TimeSpan.FromMinutes(siloTimeMinutes);
            }
            var projected = ratePerSec * contractLength + farm.EggsPaidFor + ratePerSec * TimeSinceUpdate.TotalSeconds;

            TimeSpan? timeleft = null;

            var started = DateTimeOffset.FromUnixTimeSeconds(farm.TimeAccepted);
            var ends = started.AddSeconds(contractLength);
            timeleft = (ends - DateTimeOffset.Now);
            prefarm.Started = started;

            if(!farm.CoopAllowed)
                projected = ratePerSec * (ends - DateTime.Now).TotalSeconds + farm.EggsPaidFor + ratePerSec * TimeSinceUpdate.TotalSeconds;



            if(!string.IsNullOrEmpty(farm.CoopId)) {
                var coopEnds = DateTimeOffset.FromUnixTimeSeconds(farm.CoopSharedEndTime);
                if(coopEnds > DateTimeOffset.Now) {
                    var contractLeft = (coopEnds - DateTimeOffset.Now).TotalSeconds;
                    projected = ratePerSec * contractLeft + farm.EggsPaidFor + ratePerSec * TimeSinceUpdate.TotalSeconds;
                } else {
                    var sleepTime = Math.Max(TimeSinceUpdate.TotalSeconds - (ends - DateTimeOffset.Now).TotalSeconds, 0);
                    projected = farm.EggsPaidFor + ratePerSec * sleepTime;
                }
            }


            prefarm.EggsPaidFor = farm.EggsPaidFor;
            prefarm.NumChickens = farm.NumChickens;
            prefarm.Rate = ratePerSec;
            prefarm.Projected = Math.Max(projected, 0);
            prefarm.Goal = goal;
            prefarm.Tokens = (ushort)(farm.BoostTokensReceived - farm.BoostTokensGiven - farm.BoostTokensSpent);
            prefarm.BoostTokensSpent = farm.BoostTokensSpent;
            prefarm.TimeSinceUpdate = TimeSinceUpdate;
            prefarm.TimeLeft = timeleft;
            prefarm.OfflineEggs = ratePerSec * TimeSinceUpdate.TotalSeconds;
            //prefarm.ProjectedPercent = (prefarm.Projected / goal) * 100;


            var current = farm.EggsPaidFor;
            var finished = current >= goal;
            prefarm.Completed = prefarm.Completed || finished;

            return prefarm;
        }

        public static string ShortenTime(this string str) {
            str = str.Replace(" milliseconds", "ms");
            str = str.Replace(" millisecond", "ms");
            str = str.Replace(" seconds", "s");
            str = str.Replace(" second", "s");
            str = str.Replace(" minutes", "m");
            str = str.Replace(" minute", "m");
            str = str.Replace(" hours", "h");
            str = str.Replace(" hour", "h");
            str = str.Replace(" days", "d");
            str = str.Replace(" day", "d");
            str = str.Replace(" weeks", "w");
            str = str.Replace(" week", "w");
            str = str.Replace(" months", "mo");
            str = str.Replace(" month", "mo");
            str = str.Replace(" years", "y");
            str = str.Replace(" year", "y");
            return str.Replace(" ", "");
        }

        public class GuildUser {
            public DateTimeOffset? OnBreakSince { get; set; }
            public string EggIncId { get; set; }
            public Guid DatabaseId { get; set; }
            public string DiscordName { get; set; }
            public ulong DiscordId { get; set; }
            public string Mention { get; set; }

            public GuildUser() {
            }
            public GuildUser(LeaderboardUser u) {
                DatabaseId = u.User.Id;
                DiscordName = u.User.DiscordUsername;
                DiscordId = u.DiscordUser.Id;
                Mention = u.DiscordUser.Mention;
                EggIncId = u.Backup.EggIncId;
                OnBreakSince = u.User.OnBreakSince;
            }
        }

    }
}
