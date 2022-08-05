using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using EGG9000.Bot.EggIncAPI;

using EGG9000.Common.Helpers;

using Google.Protobuf.Collections;

using MessagePack;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomBackup {
        //public bool Unchanged { get; set; }
        [Key(0)]
        public List<CustomFarm> Farms { get; set; }
        [Key(1)]
        public string EggIncId { get; set; }
        [Key(2)]
        public string UserName { get; set; }
        //[Key(3)]
        //public double EarningsBonus { get; set; }
        [Key(4)]
        public long LastBackupTime { get; set; }
        [Key(5)]
        public List<CustomResearch> EpicResearch { get; set; }
        [Key(6)]
        public ushort PermitLevel { get; set; }
        [Key(7)]
        public DateTime CacheAdded { get; set; }
        [Key(8)]
        public ushort EggsOfProphecy { get; set; }
        [Key(9)]
        public double SoulEggs { get; set; }
        [Key(10)]
        public double CurrentMultiplier { get; set; }
        //[Key(11)]
        //public List<string> CompleteContracts { get; set; }
        [Key(12)]
        public bool EmptyBackup { get; set; }
        [Key(13)]
        public List<CustomArchivedFarms> ArchivedFarms { get; set; }
        [Key(14)]
        public ulong NumPrestiges { get; set; }
        [Key(15)]
        public List<SpaceMission> SpaceMissions { get; set; }

        [Key(16)]
        public uint NumDailyGiftsCollected { get; set; }

        [IgnoreMember]
        public uint PEFromDailyGifts {
            get {
                return Math.Min(24, NumDailyGiftsCollected / 28);
            }
        }

        [Key(17)]
        public List<uint> EggMedalLevel { get; set; }

        [Key(18)]
        public ulong GoldenEggsEarned { get; set; }
        [Key(19)]
        public ulong GoldenEggsSpent { get; set; }
        [Key(20)]
        public ulong PiggyBank { get; set; }
        [Key(21)]
        public ulong DroneTakedowns { get; set; }
        [Key(22)]
        public ulong DroneTakedownsElite { get; set; }
        [Key(23)]
        public ulong NumPiggyBreaks { get; set; }
        [Key(24)]
        public List<ArtifactCount> ArtifactHall { get; set; }
        [Key(25)]
        public bool HyperloopPurchased { get; set; }
        [Key(26)]
        public uint TankLevel { get; set; }

        [IgnoreMember]
        public ulong TotalGEInPiggyBank {
            get {
                try {
                    return (ulong)(PiggyBank * ((NumPiggyBreaks + 2.0m) / 10 + 1));
                } catch (OverflowException) {
                    return UInt64.MaxValue;
                }
            }
        }

        [IgnoreMember]
        public int PEFromTrophies {
            get {
                if(EggMedalLevel is null)
                    return -1;
                if(EggMedalLevel.Count != 19)
                    throw new Exception($"Unexpected number of trophies, should be 19 but instead got {EggMedalLevel.Count}");
                int count = 0;

                if(EggMedalLevel[(int)Ei.Egg.Edible - 1] >= (uint)TrophyLevel.Diamond) count += 5;
                if(EggMedalLevel[(int)Ei.Egg.Superfood - 1] >= (uint)TrophyLevel.Diamond) count += 4;
                if(EggMedalLevel[(int)Ei.Egg.Medical - 1] >= (uint)TrophyLevel.Diamond) count += 3;
                if(EggMedalLevel[(int)Ei.Egg.RocketFuel - 1] >= (uint)TrophyLevel.Diamond) count += 2;

                if(EggMedalLevel[(int)Ei.Egg.SuperMaterial - 1] >= (uint)TrophyLevel.Diamond) count += 1;
                if(EggMedalLevel[(int)Ei.Egg.Fusion - 1] >= (uint)TrophyLevel.Diamond) count += 1;
                if(EggMedalLevel[(int)Ei.Egg.Quantum - 1] >= (uint)TrophyLevel.Diamond) count += 1;
                if(EggMedalLevel[(int)Ei.Egg.Immortality - 1] >= (uint)TrophyLevel.Diamond) count += 1;
                if(EggMedalLevel[(int)Ei.Egg.Tachyon - 1] >= (uint)TrophyLevel.Diamond) count += 1;

                if(EggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Diamond) count += 10;
                if(EggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Platinum) count += 5;
                if(EggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Gold) count += 3;
                if(EggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Silver) count += 2;
                if(EggMedalLevel[(int)Ei.Egg.Enlightenment - 1] >= (uint)TrophyLevel.Bronze) count += 1;

                return count;
            }
        }

        [IgnoreMember]
        public List<ArtifactCount> GetAvailableArtifacts {
            get {
                if(ArtifactHall is null || ArtifactHall.Count == 0) {
                    return new List<ArtifactCount>();
                }
                if(ArtifactHall.First().Artifact.Stones is null) {
                    return ArtifactHall;
                }
                var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact }).ToList();
                Farms.ForEach(f => f.Artifacts.ForEach(a => artifacts.First(x => x.Artifact.Equals(a)).Count--));
                return artifacts.Where(x => x.Count > 0).ToList();
            }
        }

        public CustomBackup() { }
        public CustomBackup(Ei.Backup backup) {
            if(backup?.Game == null) {
                EmptyBackup = true; 
                return;
            }
            EpicResearch = backup.Game.EpicResearch.Select(x => new CustomResearch(x)).ToList();
            CurrentMultiplier = backup.Game.CurrentMultiplier;
            EggIncId = backup.GetID();
            UserName = backup.UserName;
            //EarningsBonus = backup.Game.EarningsBonus;
            LastBackupTime = (long)backup.Settings.LastBackupTime;
            PermitLevel = (ushort)backup.Game.PermitLevel;
            SoulEggs = backup.Game.SoulEggsTotal;
            EggsOfProphecy = (ushort)backup.Game.EggsOfProphecy;
            NumPrestiges = backup.Stats.NumPrestiges;

            GoldenEggsEarned = backup.Game.GoldenEggsEarned;
            GoldenEggsSpent = backup.Game.GoldenEggsSpent;
            PiggyBank = backup.Game.PiggyBank;
            NumPiggyBreaks = backup.Stats.NumPiggyBreaks;
            DroneTakedowns = backup.Stats.DroneTakedowns;
            DroneTakedownsElite = backup.Stats.DroneTakedownsElite;
            HyperloopPurchased = backup.Game.HyperloopStation;
            TankLevel = backup.Artifacts.TankLevel;

            Farms = new List<CustomFarm>();
            foreach(var farm in backup.Farms) {
                AddFarm(farm, backup);
            }

            //CompleteContracts = new List<string>();
            ArchivedFarms = new List<CustomArchivedFarms>();
            CheckForCompleteContracts(backup.Contracts.Contracts);
            CheckForCompleteContracts(backup.Contracts.Archive);


            SpaceMissions = backup.ArtifactsDb?.MissionInfos?.Select(m => new SpaceMission {
                Ship = m.Ship,
                Duration = m.DurationType,
                Status = m.Status,
                DurationSeconds = (long)m.DurationSeconds,
                StartTime = (long)m.StartTimeDerived,
                Fuels = m.Fuel.Select(f => new SpaceMissionFuel {
                    Amount = f.Amount,
                    Egg = f.Egg
                }).ToList()
            }).ToList();

            NumDailyGiftsCollected = backup.Game.NumDailyGiftsCollected;

            EggMedalLevel = backup.Game.EggMedalLevel.ToList();

            ArtifactHall = backup.ArtifactsDb.InventoryItems.Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact is not null) {
                    artifact.Stones = x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null).ToList();
                }
                backup.ArtifactsDb.ArtifactStatus.First(x => true).
                return new ArtifactCount { Count = (int)x.Quantity, Artifact = artifact };
            }).ToList();
        }

        private void AddFarm(Ei.Backup.Types.Simulation farm, Ei.Backup backup) {
            var contract = backup.Contracts.Contracts.FirstOrDefault(x => x.Contract.Identifier == farm.ContractId)
    ?? backup.Contracts.Archive.FirstOrDefault(x => x.Contract.Identifier == farm.ContractId);
            var customFarm = new CustomFarm {
                FarmType = farm.FarmType,
                ContractId = farm.ContractId,
                EggsPaidFor = farm.EggsPaidFor,
                League = contract?.League,
                CoopId = contract?.CoopIdentifier,
                Cancelled = contract?.Cancelled ?? false,
                Completed = contract != null ? contract.NumGoalsAchieved == contract.Contract.Goals.Count : false,
                NumChickens = farm.NumChickens,
                CommonResearch = farm.CommonResearch.Select(x => new CustomResearch(x)).ToList(),
                EggType = farm.EggType,
                Vehicles = farm.Vehicles.ToList(),
                TrainLength = farm.TrainLength.ToList(),
                SilosOwned = farm.SilosOwned,
                TimeAccepted = (long)(contract?.TimeAccepted ?? 0),
                CoopAllowed = contract?.Contract.CoopAllowed ?? false,
                CoopSharedEndTime = (long)(contract?.CoopSharedEndTime ?? 0),
                BoostTokensReceived = (ushort)farm.BoostTokensReceived,
                BoostTokensGiven = (ushort)farm.BoostTokensGiven,
                BoostTokensSpent = (ushort)farm.BoostTokensSpent,
                CashEarned = farm.CashEarned,
                CashSpent = farm.CashSpent,
                TimeCheatDebt = (long)farm.TimeCheatDebt,
                BoostsUsed = (ushort)(contract?.BoostsUsed ?? 0),
                TimeCheatsDetected = (ushort)farm.TimeCheatsDetected,
                Habs = farm.Habs.Select(x => (ushort)x).ToList(),
                LastStepTime = (float)farm.LastStepTime
            };
            customFarm.Artifacts = new List<EggIncArtifactInstance>();
            var farmIndex = backup.Farms.IndexOf(farm);
            if(backup.ArtifactsDb != null) {
                var activeArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets[farmIndex].Slots.Where(x => x.Occupied);
                var activeArtifacts = activeArtifactSlots.Select(x => backup.ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));

                customFarm.Artifacts.AddRange(activeArtifacts.Where(x => x != null).Select(x => {
                    var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                    if(artifact == null)
                        return null;
                    artifact.Stones = x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null).ToList();
                    return artifact;
                }).Where(x => x != null));

                //customFarm.Artifacts.AddRange(activeArtifacts.Where(x => x != null)
                //    .SelectMany(x => backup.ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId)?.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y))));
                customFarm.Artifacts = customFarm.Artifacts.Where(x => x != null).ToList();
            }



            Farms.Add(customFarm);
        }

        private void CheckForCompleteContracts(RepeatedField<Ei.LocalContract> contracts) {
            foreach(var contract in contracts) {
                //if(!Farms.Any(f => f.ContractId == contract.Contract.Identifier)) {
                ArchivedFarms.Add(new CustomArchivedFarms(contract));
                //}
                //RepeatedField<Ei.Contract.Types.Goal> goals = contract.Contract.GoalSets.Count == 0 ? contract.Contract.Goals : contract.Contract.GoalSets[(int)contract.League].Goals;
                //if(contract.NumGoalsAchieved == goals.Count) {
                //    CompleteContracts.Add(contract.Contract.Identifier);
                //}
            }
        }

        [IgnoreMember]
        public double SoulEggBonus { get { return EpicResearch is null ? 0 : (double)(EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10; } }
        [IgnoreMember]
        public double ProphecyEggBonus { get { return EpicResearch is null ? 0 : ((double)(EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1; } }
        [IgnoreMember]
        public double EarningsBonus { get { return SoulEggs * SoulEggBonus * Math.Pow(ProphecyEggBonus, EggsOfProphecy); } }
    }

    [MessagePackObject]
    public class CustomResearch {
        [Key(0)]
        public string Id { get; set; }
        [Key(1)]
        public uint Level { get; set; }

        public CustomResearch() { }
        public CustomResearch(Ei.Backup.Types.ResearchItem item) {
            Id = item.Id;
            Level = item.Level;
        }
    }

    [MessagePackObject]
    public class CustomFarm {
        [Key(0)]
        public Ei.FarmType FarmType { get; set; }
        [Key(1)]
        public string ContractId { get; set; }
        [Key(2)]
        public double EggsPaidFor { get; set; }
        [Key(3)]
        public uint? League { get; set; }
        [Key(4)]
        public string CoopId { get; set; }
        [Key(5)]
        public bool Cancelled { get; set; }
        [Key(6)]
        public bool Completed { get; set; }
        [Key(7)]
        public List<CustomResearch> CommonResearch { get; set; }
        [Key(8)]
        public ulong NumChickens { get; set; }
        [Key(10)]
        public Ei.Egg EggType { get; set; }
        [Key(11)]
        public List<uint> TrainLength { get; set; }
        [Key(12)]
        public List<uint> Vehicles;
        [Key(13)]
        public List<EggIncArtifactInstance> Artifacts { get; set; }
        [Key(14)]
        public uint SilosOwned { get; set; }
        [Key(15)]
        public long TimeAccepted { get; set; }
        [Key(16)]
        public bool CoopAllowed { get; set; }
        [Key(17)]
        public long CoopSharedEndTime { get; set; }
        [Key(18)]
        public ushort BoostTokensReceived { get; set; }
        [Key(19)]
        public ushort BoostTokensGiven { get; set; }
        [Key(20)]
        public ushort BoostTokensSpent { get; set; }
        [Key(21)]
        public double CashEarned { get; set; }
        [Key(22)]
        public double CashSpent { get; set; }
        [Key(23)]
        public long TimeCheatDebt { get; set; }
        [Key(24)]
        public ushort BoostsUsed { get; set; }
        [Key(25)]
        public ushort TimeCheatsDetected { get; set; }

        //[Key(26)]
        //public Double CurrentShippingRate { get; set; }
        //[Key(27)]
        //public Double EggLayingRate { get; set; }
        //[Key(28)]
        //public Double MaxShippingRate { get; set; }
        //[Key(29)]
        //public Double EggValue { get; set; }
        //[Key(30)]
        //public Double Income { get; set; }
        //[Key(31)]
        //public Double MaxRunningBonus { get; set; }
        [Key(32)]
        public List<ushort> Habs { get; set; }
        [Key(33)]
        public float LastStepTime { get; set; }

        [IgnoreMember]
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }

        private CustomFarmStats _stats = null;
        public CustomFarmStats WithStats(CustomBackup backup) {
            if(_stats == null) {
                var eggLayingResearch = Research.GetEggLayingRatePerSec(this, backup.EpicResearch);
                var eggLayingArtifact = EggIncArtifacts.GetEggLayingRateMultiple(this);

                _stats = new CustomFarmStats();
                _stats.MaxShippingRate = Research.GetShippingCapacityPerSec(this, backup.EpicResearch) * EggIncArtifacts.GetShippingMultiple(this);
                _stats.EggLayingRate = eggLayingResearch * eggLayingArtifact;
                _stats.CurrentShippingRate = Math.Min(_stats.MaxShippingRate, _stats.EggLayingRate);
                _stats.EggValue = Research.GetEggValue(this, backup.EpicResearch) * EggIncArtifacts.GetEggValueMutiple(this);
                _stats.Income = _stats.CurrentShippingRate * _stats.EggValue * (backup.EarningsBonus / 100) * backup.CurrentMultiplier;
                _stats.MaxRunningBonus = Research.MaxRunningBonus(this, backup.EpicResearch) + EggIncArtifacts.GetMaxRunningBonusAdditive(this);
                _stats.HabSpace = Research.GetHabSpace(this, backup.EpicResearch) * EggIncArtifacts.GetHabSpaceMultiple(this);
                _stats.InternalHatchery = (int)(Research.InternalHatchery(this, backup.EpicResearch) * EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.InternalHatchery, this));
            }
            return _stats;
        }
    }

    public class CustomFarmStats {
        public Double CurrentShippingRate { get; set; }
        public Double EggLayingRate { get; set; }
        public Double MaxShippingRate { get; set; }
        public Double EggValue { get; set; }
        public Double Income { get; set; }
        public Double MaxRunningBonus { get; set; }
        public Double HabSpace { get; set; }
        public int InternalHatchery { get; set; }
    }

    [MessagePackObject]
    public class CustomArchivedFarms {
        [Key(0)]
        public string CoopName { get; set; }
        [Key(1)]
        public string ContractId { get; set; }
        [Key(2)]
        public float TimeAccepted { get; set; }
        [Key(3)]
        public bool Completed { get; set; }
        [Key(4)]
        public uint? League { get; set; }
        [Key(5)]
        public uint PEPossible { get; set; }
        [Key(6)]
        public uint PEGained { get; set; }

        [IgnoreMember]
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }

        public CustomArchivedFarms() { }
        public CustomArchivedFarms(Ei.LocalContract localContract) {
            CoopName = localContract.CoopIdentifier;
            ContractId = localContract.Contract.Identifier;
            TimeAccepted = (float)localContract.TimeAccepted;
            Completed = localContract.Completed;
            League = localContract.League;


            var goals = localContract.Contract.Goals;
            if(localContract.Contract.GoalSets is not null && localContract.Contract.GoalSets.Count > localContract.League)
                goals = localContract.Contract.GoalSets[(int)localContract.League].Goals;

            PEPossible += (uint)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy).Sum(x => x.RewardAmount);
            PEGained += (uint)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy && goals.IndexOf(x) < localContract.NumGoalsAchieved).Sum(x => x.RewardAmount);
        }
    }

    [MessagePackObject]
    public class SpaceMission {
        [Key(0)]
        public Ei.MissionInfo.Types.Spaceship Ship { get; set; }
        [Key(1)]
        public Ei.MissionInfo.Types.DurationType Duration { get; set; }
        [Key(2)]
        public Ei.MissionInfo.Types.Status Status { get; set; }
        [Key(3)]
        public List<SpaceMissionFuel> Fuels { get; set; }
        [Key(4)]
        public long DurationSeconds { get; set; }
        [Key(5)]
        public long StartTime { get; set; }

        [IgnoreMember]
        public long ReturnTime {
            get {
                return StartTime + DurationSeconds;
            }
        }
    }

    [MessagePackObject]
    public class SpaceMissionFuel {
        [Key(0)]
        public Ei.Egg Egg { get; set; }
        [Key(1)]
        public double Amount { get; set; }
    }

    public enum TrophyLevel {
        Bronze = 1,
        Silver = 2,
        Gold = 3,
        Platinum = 4,
        Diamond = 5,
    }
}
