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

namespace EGG9000.Common.Helpers {
    public static class Prefarm {
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
            public double ProjectedPercent { get; set; }
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
        }

        public static List<UserPreFarm> GetPrefarmers(List<LeaderboardUser> backups, Contract contract) {
            var startedContract = backups
                .Where(x => x.Backup != null && (x.Backup.Farms.Any(y => y.ContractId == contract.ID) || (x.Backup.ArchivedFarms?.Any(f => f.ContractId == contract.ID) ?? false)))
                .OrderByDescending(x =>
                    x.Backup.Farms.FirstOrDefault(y =>
                        y.ContractId == contract.ID
                    )?.EggsPaidFor
                );

            var users = startedContract.Select(x => {
                var prefarm = BackupToPreFarm(x, contract);
                return prefarm;
            }).Where(x => x != null && x.DiscordId != 0).OrderByDescending(x => x.Projected).ToList();


            return users; 
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

        public class CoopsBreakdown {
            public List<CoopDetails> Coops { get; set; }
            public CoopDetails AlreadyInCoop { get; set; }
            public CoopDetails Completed { get; set; }
            public List<UserPreFarm> ExpiredFarms { get; set; }

            public List<UserPreFarm> AllPreFarms { get; set; }
            //public List<SocketGuildUser> StartersNotPrefarming { get; set; }
        }

        public class CoopDetails {
            public SocketGuildUser Starter { get; set; }
            //public string StarterStatus { get; set; }
            public List<UserPreFarm> Users { get; private set; }
            public Coop Coop { get; set; }
            //public  double Projected { get; private set; }
            public double PercentProjected { get; private set; }
            public TimeSpan TimeRemaining { get; init; }

            private uint _maxSize = 0;
            public bool HasSpots { get { return Users.Count < _maxSize; } }
            public bool IsFire;
            public bool IsDoubleFire;
            public CoopDetails(List<UserPreFarm> users, double targetAmount, Coop coop = null, uint MaxSize = 0) {
                Users = users;
                if(targetAmount > 0) {
                        TimeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, users);
                    //Projected = users.Sum(x => x.Projected) / targetAmount;
                    PercentProjected = (users.Sum(x => x.Projected) / targetAmount) * 100;
                    if(TimeRemaining < TimeSpan.FromHours(18)) {
                        IsDoubleFire = true;
                    } else if(TimeRemaining < TimeSpan.FromHours(36)) {
                        IsFire = true;
                    }

                }
                Coop = coop;
                _maxSize = MaxSize;
            }
        }

        public static async Task<CoopsBreakdown> GetBreakdown(ApplicationDbContext db, GuildContract guildContract) {
            var dbusers = await db.DBUsers.AsQueryable().Where(x => !x.TempDisabled && x.GuildId == guildContract.GuildID).ToListAsync();
            var backups = dbusers.Where(x => x.Backups != null && x.GuildId == guildContract.GuildID).SelectMany(y => y.Backups.Select(x => new LeaderboardUser {
                User = y,
                Backup = x
            })).ToList();

            var prefarmers = GetPrefarmers(backups, guildContract.Contract);
            var coops = await db.Coops.Include(x => x.UserCoopsXrefs).ThenInclude(x => x.User).Where(x => x.Created > DateTimeOffset.Now.AddMonths(-6) && x.ContractID == guildContract.ContractID && x.GuildId == guildContract.GuildID && x.League == (guildContract.Elite ? 0 : 1)).ToListAsync();
            var users = prefarmers.Where(x => x.Elite == guildContract.Elite && x.NumChickens > 0).ToList();
            users = users.Where(x => !coops.Any(c => c.UserCoopsXrefs.Any(xr => xr.EggIncId == x.EggIncId || xr.RefEggIncId == x.EggIncId))).ToList();
            var coopsBreakdown = Prefarm.GetBreakdown(users, guildContract);
            return coopsBreakdown;
        }

        public static CoopsBreakdown GetBreakdown(List<UserPreFarm> users, GuildContract guildContract) {
            var completed = users.Where(x => x.Completed).OrderBy(x => x.Name).ToList();
            var currentUsers = users.Where(x => !x.Completed && !x.CancelledFarm).ToList();

            var alreadyInCoop = currentUsers.Where(x => !string.IsNullOrEmpty(x.CoopName)).OrderBy(x => x.Name).ToList();
            var notInCoop = currentUsers.Where(x => string.IsNullOrEmpty(x.CoopName)).ToList();

            var coopsBreakdown = new CoopsBreakdown {
                Coops = new List<CoopDetails>(),
                AlreadyInCoop = new CoopDetails (alreadyInCoop ,0),
                Completed = new CoopDetails (completed,0)
            };

            coopsBreakdown.ExpiredFarms = notInCoop.Where(x => x.TimeLeft == null || x.TimeLeft.Value.TotalSeconds <= 0).ToList();
            notInCoop.RemoveAll(x => coopsBreakdown.ExpiredFarms.Any(expired => expired.EggIncId == x.EggIncId));


            var numPerCoop = Math.Max(guildContract.Contract.Details.MaxCoopSize, 1);// - 1;
            var numOfCoops = Math.Ceiling((decimal)notInCoop.Count() / numPerCoop);
            var size = guildContract.NumberOfCoops;

            if(size > 0) {
                numOfCoops = Math.Max(size, numOfCoops);
            }


            var coops = new List<List<UserPreFarm>>();


            for(int i = 1; i <= numOfCoops; i++) {
                coops.Add(new List<UserPreFarm>());
            }

            var coopsNeedingStarter = coops.ToList();


            if(numPerCoop >= 4) {
                var groupedAccounts = notInCoop.GroupBy(x => x.DatabaseId);
                foreach(var groupedAccount in groupedAccounts.Where(x => x.Count() > 1)) {
                    var accounts = groupedAccount.ToList();
                    int allowedAccounts = (int)numPerCoop / 4 + 1;
                    if(groupedAccount.Count() > allowedAccounts) {
                        accounts = groupedAccount.OrderBy(x => x.Backup.EarningsBonus).Take(allowedAccounts).ToList();
                    }
                    if(accounts.Count() * 4 <= numPerCoop) {
                        var smallestCoop = coops.Where(x => x.Count < numPerCoop - accounts.Count()).OrderBy(x => x.Sum(y => y.Projected)).First();
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
                List<UserPreFarm> smallestCoop;
                if(u.Projected < targetAmount / 100) {
                    smallestCoop = coops.OrderBy(x => x.Count).ThenBy(x => x.Sum(y => y.Projected)).First();
                } else {
                    smallestCoop = coops.Where(x => x.Count < numPerCoop).OrderBy(x => x.Sum(y => y.Projected)).FirstOrDefault();
                    if(smallestCoop == null) {
                        smallestCoop = new List<UserPreFarm>();
                        coops.Add(smallestCoop);
                    }
                }
                smallestCoop.Add(u);
            });

            for(int i = 1; i <= coopsNeedingStarter.Count; i++) {
                var details = new CoopDetails(coopsNeedingStarter[i - 1], targetAmount);
                coopsBreakdown.Coops.Add(details);
            }

            coopsBreakdown.Coops = coopsBreakdown.Coops.OrderByDescending(x => x.Users.Sum(x => x.Projected)).ToList();
            coopsBreakdown.AllPreFarms = users;
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

                        //var lUser = new LeaderboardUser { Backup = (await ContractsAPI.FirstContact(c.UserId)).Backup };
                        //if(lUser.Backup != null)
                        //    prefarm = BackupToPreFarm(lUser, contract);
                        //if(prefarm == null)
                        //Console.WriteLine($"Missing prefarm for {c.UserName}");
                    }
                    if(prefarm != null)
                        prefarms.Add(prefarm);
                }
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


            prefarms.ForEach(x => {
                if(!string.IsNullOrWhiteSpace(x.Coop) && x.Coop.ToLower() != coop.Name.ToLower() && !x.CancelledFarm && !x.Coop.StartsWith("✔️") && !x.Coop.StartsWith("❌") && !x.Coop.Contains("Different")) {
                    //x.Coop += " (Different Coop)";
                } else {
                    var joined = (coop.LastStatusUpdate?.Contributors.Any(y => y.UserId == x.EggIncId) ?? false || coop.UserCoopsXrefs.Any(y => y.JoinedCoop && y.UserId == x.DatabaseId));
                    if(coop.Status == CoopStatusEnum.Failed && string.IsNullOrEmpty(x.CoopName)) {
                        x.Coop = "";
                    } else {
                        x.Coop = joined ? "✔️" : $"❌{x.TimeLeft?.Humanize(precision: 2).ShortenTime().Replace(" ", "").Replace(",", "")}";
                        if(x.Name.StartsWith("*")) {
                            x.Coop = "👽";
                            x.Name = x.Name.Substring(1);
                        }
                    }
                }

            });

            return prefarms;
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
            prefarm.Tokens = (ushort)(farm.BoostTokensReceived - farm.BoostTokensGiven - farm.BoostTokensSpent);
            prefarm.BoostTokensSpent = farm.BoostTokensSpent;
            prefarm.TimeSinceUpdate = TimeSinceUpdate;
            prefarm.TimeLeft = timeleft;
            prefarm.OfflineEggs = ratePerSec * TimeSinceUpdate.TotalSeconds;
            prefarm.ProjectedPercent = (prefarm.Projected / goal) * 100;

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
            str = str.Replace(" months", "m");
            str = str.Replace(" month", "m");
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
