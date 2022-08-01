using Humanizer;
using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EGG9000.Bot.Services;
using EGG9000.Common.Database;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using static Ei.ContractCoopStatusResponse.Types;
using Newtonsoft.Json;

namespace EGG9000.Common.Helpers {
    public static class Prefarm {
        public class UserWithBackup {
            public DBUser User { get; set; }
            public CustomBackup Backup { get; set; }

        }
        public class LeaderboardUser {
            public DBUser User { get; set; }
            public CustomBackup Backup { get; set; }
            public DateTimeOffset? lastSeen { get; set; }
            public int RecentContracts { get; set; }
            public int ActiveContracts { get; set; }
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
            public bool Active { get; set; }
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
            public bool Elite { get; set; }
            public bool Completed { get; set; } = false;

            public CustomBackup Backup { get; set; }

            public bool PotentialBoxCarry { get; set; }
            public UserCoopXref Xref { get; set; }
            public ContributionInfo ContributionInfo { get; set; }
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
            return TimeSpan.FromSeconds(remainingSeconds).Humanize(precision: 2).ShortenTime();
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
            return TimeSpan.FromSeconds(remainingSeconds);
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
            public TimeSpan TimeRemaining { get; private set; }

            private uint _maxSize = 0;
            public bool HasSpots { get { return CoopParticipants.Count < _maxSize; } }
            public bool IsFire;
            public bool IsDoubleFire;
            public CoopDetails(Coop coop, GuildContract guildContract, IList<UserWithBackup> backups, DiscordSocketClient discord, Ei.ContractCoopStatusResponse status = null) {
                var coopParticipants = GetCoopParticipants(coop, guildContract.Contract, status ?? coop.LastStatusUpdate, backups, discord);
                Coop = coop;
                SetCoopDetails(coopParticipants, guildContract);
            }
            public CoopDetails(List<UserFarmDetails> coopParticipants, GuildContract guildContract) {
                SetCoopDetails(coopParticipants, guildContract);
            }
            public void SetCoopDetails(List<UserFarmDetails> coopParticipants, GuildContract guildContract) {
                CoopParticipants = coopParticipants;
                var league = guildContract.Elite ? 0 : 1;
                var targetAmount = guildContract.Contract.Details.GoalSets[league].Goals.Last().TargetAmount;
                if(targetAmount > 0) {
                    TimeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, CoopParticipants);
                    PercentProjected = (CoopParticipants.Sum(x => x.Projected) / targetAmount) * 100;
                    if(TimeRemaining < TimeSpan.FromHours(18)) {
                        IsDoubleFire = true;
                    } else if(TimeRemaining < TimeSpan.FromHours(36)) {
                        IsFire = true;
                    }

                }
                _maxSize = (uint)guildContract.Contract.MaxUsers;
            }

        }

        public static async Task<CoopsBreakdown> GetBreakdown(ApplicationDbContext db, GuildContract guildContract, DiscordSocketClient discord) {
            var dbusers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildContract.GuildID).ToListAsync();
            var backups = dbusers.Where(x => x.Backups != null && x.GuildId == guildContract.GuildID).SelectMany(y => y.Backups.Select(x => new UserWithBackup {
                User = y,
                Backup = x
            })).ToList();

            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).Where(x => x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();


            var missingXrefUsers = coops.SelectMany(c => c.UserCoopsXrefs.Where(x => !backups.Any(b => b.User.Id == x.UserId))).Select(x => x.UserId);

            var missingUsers = await db.DBUsers.Where(x => missingXrefUsers.Contains(x.Id)).ToListAsync();
            backups.AddRange(missingUsers.SelectMany(u => u.Backups.Select(b => new UserWithBackup { User = u, Backup = b })));

            var coopsBreakdown = Prefarm.GetBreakdown(coops, backups, guildContract, discord);
            return coopsBreakdown;
        }

        public static CoopsBreakdown GetBreakdown(List<Coop> coops, List<UserWithBackup> usersWithBackups, GuildContract guildContract, DiscordSocketClient discord) {
            coops = coops.Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6)).ToList();

            var coopsBreakdown = new CoopsBreakdown {
                ExistingCoops = coops.Select(c => new CoopDetails(c, guildContract, usersWithBackups, discord, c.LastStatusUpdate)).ToList()
            };

            var notAssignedCoop = usersWithBackups
                .Where(x =>
                    !coopsBreakdown.ExistingCoops.Any(c => c.CoopParticipants.Any(p => (p.Xref?.EggIncId) == x.Backup.EggIncId))
                )
                .Select(x => new UserFarmDetails(guildContract.Contract, x, discord));

            notAssignedCoop = notAssignedCoop.Where(x => x.Backup != null && !x.DBUser.TempDisabled && x.DBUser.GuildId == guildContract.GuildID &&
                    (
                        x.Backup.Farms.Any(y => y.ContractId == guildContract.ContractID && (y.Completed || (guildContract.Elite ? y.League == 0 : y.League == 1))) ||
                        (x.Backup.ArchivedFarms?.Any(f => f.ContractId == guildContract.ContractID && (f.Completed || (guildContract.Elite ? f.League == 0 : f.League == 1))) ?? false)
                    )
                )
                .OrderByDescending(x => x.Projected);

            notAssignedCoop = notAssignedCoop.Where(x => (x.Elite == guildContract.Elite || x.Completed) && (x.Farm is not null || x.ArchivedFarm is not null));

            var completed = notAssignedCoop.Where(x => x.Completed).OrderBy(x => x.Name).ToList();
            var currentUsers = notAssignedCoop.Where(x => !x.Completed && !x.CancelledFarm).ToList();

            var alreadyInCoop = currentUsers.Where(x => x.InCoop).OrderBy(x => x.Name).ToList();
            var notInCoop = currentUsers.Where(x => !x.InCoop && x.NumChickens > 500).ToList();

            coopsBreakdown.AlreadyInCoop = alreadyInCoop;
            coopsBreakdown.Completed = completed;

            coopsBreakdown.ExpiredFarms = notInCoop.Where(x => x.TimeLeft.TotalSeconds <= 0).ToList();
            notInCoop.RemoveAll(x => coopsBreakdown.ExpiredFarms.Any(expired => expired.EggIncId == x.EggIncId));


            var numPerCoop = Math.Max(guildContract.Contract.Details.MaxCoopSize, 1);
            var numOfCoops = Math.Ceiling((decimal)notInCoop.Count() / numPerCoop);
            var size = guildContract.NumberOfCoops;

            if(size > 0) {
                numOfCoops = Math.Max(size, numOfCoops);
            }


            var potentialCoops = new List<List<UserFarmDetails>>();


            for(int i = 1; i <= numOfCoops; i++) {
                potentialCoops.Add(new List<UserFarmDetails>());
            }

            if(numPerCoop >= 4) {
                var groupedAccounts = notInCoop.GroupBy(x => x.DBUser.Id).Where(x => x.Count() > 1);
                foreach(var groupedAccount in groupedAccounts) {
                    var accounts = groupedAccount.ToList();
                    var allowedAccounts = numPerCoop / 4 + 1;
                    if(groupedAccount.Count() > allowedAccounts) {
                        accounts = groupedAccount.OrderBy(x => x.Backup.EarningsBonus).Take((int)allowedAccounts).ToList();
                    }
                    if(accounts.Count() > 1) {
                        var smallestCoop = potentialCoops.Where(x => x.Count < numPerCoop - accounts.Count()).OrderBy(x => x.Sum(y => y.Projected)).First();
                        foreach(var account in accounts) {
                            smallestCoop.Add(account);
                            notInCoop.Remove(account);
                        }

                    }
                }
            }

            var league = guildContract.Elite ? 0 : 1;
            var targetAmount = guildContract.Contract.Details.GoalSets[league].Goals.Last().TargetAmount;

            notInCoop.ForEach(u => {
                List<UserFarmDetails> smallestCoop;
                if(u.Projected < targetAmount / 100) {
                    smallestCoop = potentialCoops.OrderBy(x => x.Count).ThenBy(x => x.Sum(y => y.Projected)).First();
                } else {
                    smallestCoop = potentialCoops.Where(x => x.Count < numPerCoop).OrderBy(x => x.Sum(y => y.Projected)).FirstOrDefault();
                    if(smallestCoop == null) {
                        smallestCoop = new List<UserFarmDetails>();
                        potentialCoops.Add(smallestCoop);
                    }
                }
                smallestCoop.Add(u);
            });

            coopsBreakdown.PotentialCoops = new List<CoopDetails>();
            for(int i = 1; i <= potentialCoops.Count; i++) {
                var details = new CoopDetails(potentialCoops[i - 1], guildContract);
                coopsBreakdown.PotentialCoops.Add(details);
            }

            coopsBreakdown.PotentialCoops = coopsBreakdown.PotentialCoops.OrderByDescending(x => x.PercentProjected).ToList();
            //coopsBreakdown.AllPreFarms = coopParticipants;
            return coopsBreakdown;
        }

        public static async Task<List<UserPreFarm>> GetBackupsForAliens(List<Coop> coops, List<UserPreFarm> allPrefarms, Contract contract, APILink apiLink) {
            var tasks = new List<Task<CustomBackup>>();
            foreach(var coop in coops) {
                if(coop.LastStatusUpdate != null) {
                    foreach(var c in coop.LastStatusUpdate.Contributors) {
                        var prefarm = allPrefarms.FirstOrDefault(x => x.EggIncId == c.UserId);
                        if(prefarm == null) {
                            tasks.Add(apiLink.GetBackup(c.UserId));
                        }
                    }
                }
            }
            await Task.WhenAll(tasks);
            var alienPrefarms = new List<UserPreFarm>();
            foreach(var task in tasks) {
                var lUser = new LeaderboardUser { Backup = task.Result };
                if(lUser.Backup != null) {
                    alienPrefarms.Add(BackupToPreFarm(lUser, contract));
                }
            }
            return alienPrefarms;
        }

        public static List<UserPreFarm> GetPrefarmsForCoop(Coop coop, List<UserPreFarm> allPrefarms, List<UserPreFarm> alienPrefarms, Contract contract) {
            var prefarms = new List<UserPreFarm>();

            if(coop.LastStatusUpdate != null) {
                foreach(var c in coop.LastStatusUpdate.Contributors) {
                    var prefarm = allPrefarms.FirstOrDefault(x => x.EggIncId == c.UserId);
                    if(prefarm == null) {
                        prefarm = alienPrefarms.FirstOrDefault(x => x?.EggIncId == c.UserId);
                    }
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
                            prefarm.Name = prefarm.Name.Substring(1);
                        }
                    }
                }

            });

            return prefarms;
        }

        public static List<UserFarmDetails> GetCoopParticipants(Coop coop, Contract contract, Ei.ContractCoopStatusResponse status, IEnumerable<UserWithBackup> backups, DiscordSocketClient discord) {
            var coopParticipants = new List<UserFarmDetails>();


            //Add joined participants
            if(status is not null) {
                foreach(var participant in status.Participants) {
                    var xref = coop.UserCoopsXrefs.FirstOrDefault(xref => xref.EggIncId == participant.GetID());
                    bool saveFixedUserName = false;
                    //First try for FixedUserName
                    if(xref == null) {
                        xref = coop.UserCoopsXrefs.FirstOrDefault(x => !string.IsNullOrEmpty(x.FixedUserName) && x.FixedUserName == participant.UserName);
                    }

                    if(xref == null) {
                        //Try matching to EB
                        var matchbackup = backups.FirstOrDefault(x => Math.Log10(x.Backup.EarningsBonus / 100) == participant.SoulPower);
                        xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                        if(xref is not null) saveFixedUserName = true;
                    }

                    if(xref == null && !string.IsNullOrWhiteSpace(participant.UserName)) {
                        //Now try to match a backup username
                        var matchbackup = backups.FirstOrDefault(x => x.Backup.UserName == participant.UserName);
                        xref = coop.UserCoopsXrefs.FirstOrDefault(x => x.UserId == matchbackup?.User.Id && x.EggIncId == matchbackup?.Backup.EggIncId);
                        if(xref is not null) saveFixedUserName = true;
                    }


                    if(saveFixedUserName) {
                        var isNameUnique = !backups.Any(b => b.Backup.UserName == participant.UserName && b.Backup.EggIncId != xref.EggIncId);
                        if(isNameUnique && !string.IsNullOrEmpty(participant.UserName))
                            xref.FixedUserName = participant.UserName;
                    }
                    var backup = xref is not null ? backups.FirstOrDefault(b => b.Backup.EggIncId == xref.EggIncId) : null;
                    coopParticipants.Add(new UserFarmDetails(xref, participant, contract, backup, discord));
                }
            }

            //Add missing participants
            var missingXrefs = coop.UserCoopsXrefs.Where(x => !coopParticipants.Any(y => y.Xref == x));
            coopParticipants.AddRange(missingXrefs.Select(x => {
                var backup = backups.FirstOrDefault(b => b.Backup.EggIncId == x.EggIncId);
                if(backup == null)
                    backup = backups.FirstOrDefault(b => b.User.Id == x.UserId);
                try {
                    if(x.Status is not null) {
                        var lastStatus = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(x.Status);
                        if(lastStatus is not null)
                            return new UserFarmDetails(x, lastStatus, contract, new UserWithBackup { Backup = backup?.Backup, User = backup?.User ?? x.User }, discord);
                    }
                } catch(Exception) {
                }
                if(backup == null)
                    backup = x.User?.Backups?.Select(b => new UserWithBackup { User = x.User, Backup = b }).FirstOrDefault();
                return new UserFarmDetails(x, contract, backup, discord);
            }));

            return coopParticipants;
        }

        public static UserPreFarm BackupToPreFarm(LeaderboardUser user, Contract contract) {
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
            var farmStats = farm?.WithStats(user.Backup);

            var prefarm = new UserPreFarm {
                EggIncId = user.Backup.EggIncId,
                DatabaseId = user.User?.Id,
                DiscordId = user.User?.DiscordId ?? 0,
                Name = user.User?.DiscordUsername ?? "*" + user.Backup.UserName,
                Elite = farm.League == 0,
                User = user.User,
                Coop = farm.CoopId,
                CoopName = farm.CoopId,
                CancelledFarm = farm.Cancelled,
                Backup = user.Backup,
                Completed = farm.Completed,
                EggsPaidFor = farm.EggsPaidFor
            };

            var goal = contract.Details.GoalSets[user.Elite ? 0 : 1].Goals.Last().TargetAmount;

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
            str = str.Replace(" minutes", "mi");
            str = str.Replace(" minute", "mi");
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
            public String DiscordName { get; set; }
            public ulong DiscordId { get; set; }
            public String Mention { get; set; }

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
