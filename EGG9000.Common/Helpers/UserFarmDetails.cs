using Discord.WebSocket;

using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Common.Helpers {
    public class UserFarmDetails {
        public UserCoopXref Xref { get; init; }
        public Ei.ContractCoopStatusResponse.Types.ContributionInfo CoopStatus { get; init; }
        public bool Joined { get; init; }
        public CustomBackup Backup { get; set; }
        public CustomFarmStats FarmStats { get; set; }
        public CustomFarm Farm { get; set; }
        public Contract Contract { get; set; }
        public SocketGuildUser DiscordUser { get; set; }
        public CustomArchivedFarms ArchivedFarm { get; set; }
        public DBUser DBUser { get; set; }

        public UserFarmDetails(UserCoopXref xref, Ei.ContractCoopStatusResponse.Types.ContributionInfo coopStatus, Contract contract, UserWithBackup userWithbackup, DiscordSocketClient discord) {
            if(coopStatus is null)
                throw new ArgumentNullException(null, "coopStatus");
            Xref = xref;
            CoopStatus = coopStatus;
            Contract = contract;
            Joined = true;
            if(userWithbackup is not null) {
                Backup = userWithbackup.Backup;
                Farm = Backup?.Farms.FirstOrDefault(f => f.ContractId == contract.ID);
                FarmStats = Farm?.WithStats(Backup);
                if(Farm is null)
                    ArchivedFarm = Backup?.ArchivedFarms.FirstOrDefault(f => f.ContractId == contract.ID);
                DBUser = userWithbackup.User;
                DiscordUser = DBUser.GuildId > 0 ? discord.Guilds.FirstOrDefault(x => x.Id == DBUser.GuildId).GetUser(DBUser.DiscordId) : null;
            }
        }

        public UserFarmDetails(UserCoopXref xref, Contract contract, UserWithBackup userWithbackup, DiscordSocketClient discord) {
            if(xref is null)
                throw new ArgumentNullException(null, "xref");
            if(userWithbackup is null)
                throw new ArgumentNullException(null, "userWithBackup");
            if(userWithbackup.Backup is null)
                throw new ArgumentNullException(null, "userWithBackup.Backup");
            Xref = xref;
            Contract = contract;
            Joined = false;
            if(userWithbackup is not null) {
                Backup = userWithbackup.Backup;
                Farm = Backup.Farms.FirstOrDefault(f => f.ContractId == contract.ID);
                FarmStats = Farm?.WithStats(Backup);
                if(Farm is null)
                    ArchivedFarm = Backup.ArchivedFarms.FirstOrDefault(f => f.ContractId == contract.ID);
                DBUser = userWithbackup.User;
                DiscordUser = DBUser.GuildId > 0 ? discord.Guilds.FirstOrDefault(x => x.Id == DBUser.GuildId).GetUser(DBUser.DiscordId) : null;
            }
        }

        public UserFarmDetails(Contract contract, UserWithBackup userWithbackup, DiscordSocketClient discord) {
            if(userWithbackup is null)
                throw new ArgumentNullException(null, "userWithBackup");
            if(userWithbackup.Backup is null)
                throw new ArgumentNullException(null, "userWithBackup.Backup");
            Contract = contract;
            Joined = false;
            if(userWithbackup is not null) {
                Backup = userWithbackup.Backup;
                Farm = Backup.Farms.FirstOrDefault(f => f.ContractId == contract.ID);
                FarmStats = Farm?.WithStats(Backup);
                if(Farm is null)
                    ArchivedFarm = Backup.ArchivedFarms.FirstOrDefault(f => f.ContractId == contract.ID);
                DBUser = userWithbackup.User;
                DiscordUser = DBUser.GuildId > 0 ? discord.Guilds.FirstOrDefault(x => x.Id == DBUser.GuildId).GetUser(DBUser.DiscordId) : null;
            }
        }

        public TimeSpan FarmExpires {
            get {
                return DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds(Farm?.TimeAccepted ?? (long)ArchivedFarm.TimeAccepted);
            }
        }

        public bool Elite { 
            get {
                return (Farm?.League ?? ArchivedFarm?.League ?? 0) == 0;
            } 
        }

        public double Rate {
            get {
                if(CoopStatus is not null)
                    return CoopStatus.ContributionRate;
                if(Farm is not null)
                    return Math.Min(FarmStats.EggLayingRate, FarmStats.CurrentShippingRate);
                return 0;
            }
        }

        public double EggsShipped {
            get {
                if(CoopStatus is not null)
                    return CoopStatus.ContributionAmount;
                if(Farm is not null)
                    return Farm.EggsPaidFor;
                return 0;
            }
        }

        public DateTimeOffset? Started {
            get {
                if(Farm is not null)
                    return DateTimeOffset.FromUnixTimeSeconds(Farm.TimeAccepted);
                return null;
            }
        }

        public double SiloTime {
            get {
                if(CoopStatus?.FarmInfo is not null) {
                    return Research.GetFarmSiloTime(CoopStatus.FarmInfo);
                } else if(Farm is not null) {
                    return Research.GetTotalSiloCapacity(Backup) * Farm.SilosOwned;
                } else {
                    return 6 * 60;
                }
            }
        }

        public TimeSpan OfflineTime {
            get {
                if(CoopStatus?.FarmInfo is not null) {
                    var siloTimeMinutes = Research.GetFarmSiloTime(CoopStatus.FarmInfo);
                    var timeSinceUpdate = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds((long)CoopStatus.FarmInfo.Timestamp);
                    if(timeSinceUpdate.TotalMinutes > siloTimeMinutes) {
                        timeSinceUpdate = TimeSpan.FromMinutes(siloTimeMinutes);
                    }
                    return timeSinceUpdate;
                } else if(Farm is not null) {
                    var siloTimeMinutes = Research.GetTotalSiloCapacity(Backup) * Farm.SilosOwned;
                    var timeSinceUpdate = DateTimeOffset.Now - DateTimeOffset.FromUnixTimeSeconds((long)Farm.LastStepTime);
                    if(timeSinceUpdate.TotalMinutes > siloTimeMinutes) {
                        timeSinceUpdate = TimeSpan.FromMinutes(siloTimeMinutes);
                    }
                    return timeSinceUpdate;
                } 
                return TimeSpan.Zero;
            }
        }

        private DateTimeOffset? _farmingEnds;
        public DateTimeOffset FarmingEnds {
            get {
                if(_farmingEnds is null) {
                    if(CoopStatus is not null)
                        _farmingEnds = DateTimeOffset.Now.AddSeconds(CoopStatus.TimeLeftSeconds);
                    else if(Farm is not null && Farm.CoopSharedEndTime > 0)
                        _farmingEnds = DateTimeOffset.FromUnixTimeSeconds(Farm.CoopSharedEndTime);
                    else if(Farm is not null)
                        _farmingEnds = Contract.MaxUsers > 1 ?
                            DateTimeOffset.Now.AddSeconds(Contract.length_seconds) :
                            DateTimeOffset.FromUnixTimeSeconds((Farm.TimeAccepted + (long)Contract.length_seconds));
                    else
                        _farmingEnds = DateTimeOffset.Now;
                }
                return _farmingEnds.Value;
            }
        }

        private double? _projected;
        public double Projected {
            get {
                if(_projected is null) {
                    if(FarmingEnds > DateTimeOffset.Now) {
                        var siloTimeHours = SiloTime / 60;
                        var percentToCount = Math.Min(siloTimeHours / 24, 1);

                        var contractLeft = (FarmingEnds - DateTimeOffset.Now).TotalSeconds;
                        _projected = percentToCount * Rate * contractLeft + EggsShipped + Rate * OfflineTime.TotalSeconds;
                    } else {
                        var sleepTime = Math.Max(OfflineTime.TotalSeconds - (FarmingEnds - DateTimeOffset.Now).TotalSeconds, 0);
                        _projected = EggsShipped + Rate * sleepTime;
                    }

                }
                return _projected.Value;
            }
        }

        public double ProjectedPercent {
            get {
                var target = Contract.Details.GoalSets[(int)Farm.League.Value].Goals.Max(x => x.TargetAmount);
                return Projected / target * 100;
            }
        }

        public double NumChickens {
            get {
                return CoopStatus is not null ?
                    CoopStatus.ProductionParams.FarmPopulation :
                    Farm?.NumChickens ?? 0;
            }
        }

        public string EggIncId {
            get {
                return Backup is not null ?
                    Backup.EggIncId : CoopStatus?.GetID() ?? Xref.EggIncId;
            }
        }

        public string Name {
            get {
                return DiscordUser?.DisplayName ?? CoopStatus?.UserName ?? DBUser?.DiscordUsername ?? "[error getting name]";
            }
        }


        public TimeSpan TimeLeft {
            get {
                if(CoopStatus is not null) return TimeSpan.FromSeconds(CoopStatus.TimeLeftSeconds);
                if(Farm is not null) return (DateTimeOffset.FromUnixTimeSeconds(Farm.TimeAccepted) + Contract.ContractTime) - DateTimeOffset.Now;
                return TimeSpan.Zero;
            }
        }

        public double OfflineEggs { get { return Rate * OfflineTime.TotalSeconds; } }

        public bool Completed { get { return Farm?.Completed ?? ArchivedFarm?.Completed ?? false; } }

        public bool CancelledFarm { get { return Farm?.Cancelled ?? false; } }

        public bool InCoop { get { return CoopStatus is not null || !string.IsNullOrEmpty(Farm?.CoopId); } }
    }
}
