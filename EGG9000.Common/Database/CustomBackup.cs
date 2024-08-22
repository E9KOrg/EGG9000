using EGG9000.Bot.EggIncAPI;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiStatics;
using Google.Protobuf.Collections;
using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;

using static Ei.Contract.Types;
using static Ei.MissionInfo.Types;
using static Ei.ArtifactSpec.Types;
using static Ei.GameModifier.Types;

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
        public DateTimeOffset GetLastBackupDateTime() {
            return DateTimeOffset.FromUnixTimeSeconds(LastBackupTime);
        }
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
        [Key(27)]
        public PlayerGrade Grade { get; set; }
        [Key(28)]
        public byte ClientVersion { get; set; }
        [Key(29)]
        public Dictionary<Ei.Egg, double> FuelAmounts { get; set; }
        [Key(30)]
        public double GradeProgress { get; set; }
        [Key(31)]
        public Ei.Egg MaxEggReached { get; set; }
        [Key(32)]
        public Dictionary<Ei.Egg, ulong> MaxFarmSizeReached { get; set; }
        [Key(33)]
        public bool HasDeviceId { get; set; } = false;
        [Key(34)]
        public string DeviceId { get; set; } = string.Empty;
        [Key(36)]
        public List<(Spaceship ship, DurationType type, int count)> ShipsSent { get; set; }
        [Key(37)]
        public double SeasonCS { get; set; } = 0;
        [Key(38)]
        public double TotalCS { get; set; } = 0;
        [Key(39)]
        public List<List<EggIncArtifactInstance>> ArtifactSets { get; set; } = [];
        [Key(40)]
        public double CraftingXP { get; set; } = 0;
        [Key(41)]
        public SpaceMission FuelingMission { get; set; }
        [Key(42)]
        public Dictionary<string, ulong> CustomEggMaxFarmSizeReached = [];


        /*
        * The previous formula used here was off by a level or two
        * 
        * This formula is tracking the data from:
        * https://egg-inc.fandom.com/wiki/Piggy_Bank
        * 
        * Each time the Piggy Bank is cracked it gains a bonus, 
        * starting at 2% on the first level, 25% on the second level, 
        * and 10n+10% after that point (e.g. level 6 would have a 70% bonus
        * 
        * NumPiggyBreaks = 1 for a new account, so condition is < 2 => 2% => 1.02
        * 'Second level' would be NumPiggyBreaks = 2; < 3 => 25% => 1.25
        * Otherwise => (10 * n + 10)% => ( ([that percentage] / 100) + 1) to determine the scalar
        */
        [IgnoreMember]
        public ulong TotalGEInPiggyBank {
            get {
                try {
                    return NumPiggyBreaks switch {
                        < 2 => (ulong)(PiggyBank * 1.02),
                        < 3 => (ulong)(PiggyBank * 1.25),
                        _ => PiggyBank + (PiggyBank * (10 * (NumPiggyBreaks + 1) + 10) / 100 + 1)
                    };
                } catch(OverflowException) {
                    return ulong.MaxValue;
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

        public List<ArtifactCount> GetAvailableArtifacts() {
            if(ArtifactHall is null || ArtifactHall.Count == 0) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            Farms.Where(x => x.FarmType != Ei.FarmType.Empty && x.CoopSimulationEndTime == 0).ToList().ForEach(f => f.Artifacts.ForEach(a => artifacts.First(x => x.Artifact.Equals(a)).Count--));
            return artifacts.Where(x => x.Count > 0).ToList();
        }

        public List<ArtifactCount> GetAvailableArtifacts(CustomFarm farm) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();

            var farms = Farms.Where(x => x != farm);

            farms.Where(x => x != farm && x.FarmType != Ei.FarmType.Empty && x.CoopSimulationEndTime == 0).ToList().ForEach(f => f.Artifacts.ForEach(a => artifacts.First(x => x.Artifact.Equals(a)).Count--));
            return artifacts.Where(x => x.Count > 0).ToList();
        }

        public CustomBackup() { }

        public CustomBackup(Ei.Backup backup, CustomBackup lastBackup = null) {
            if(backup?.Game == null) {
                EmptyBackup = true;
                return;
            }
            EpicResearch = backup.Game.EpicResearch.Select(x => new CustomResearch(x)).ToList();
            CurrentMultiplier = backup.Game.CurrentMultiplier;
            EggIncId = backup.GetID();
            UserName = string.IsNullOrEmpty(backup.UserName) ? lastBackup?.UserName ?? "" : backup.UserName;
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
            Grade = backup.Contracts.LastCpi?.Grade ?? PlayerGrade.GradeUnset;
            GradeProgress = backup.Contracts.LastCpi?.GradeProgress ?? 0;
            ClientVersion = (byte)backup.Version;

            TotalCS = backup.Contracts.LastCpi?.TotalCxp ?? -1;
            SeasonCS = backup.Contracts.LastCpi?.SeasonCxp ?? -1;

            HasDeviceId = backup.HasDeviceId;
            if(backup.HasDeviceId) DeviceId = backup.DeviceId;

            MaxEggReached = backup.Game.MaxEggReached;

            CraftingXP = backup.Artifacts.CraftingXp;

            Farms = new List<CustomFarm>();
            foreach(var farm in backup.Farms.Where(x => x.FarmType != Ei.FarmType.Empty)) {
                AddFarm(farm, backup);
            }

            //CompleteContracts = new List<string>();
            ArchivedFarms = new List<CustomArchivedFarms>();
            AddContracts(backup.Contracts.Contracts);
            AddContracts(backup.Contracts.Archive);


            SpaceMissions = backup.ArtifactsDb?.MissionInfos?.Select(m => new SpaceMission {
                Ship = m.Ship,
                Duration = m.DurationType,
                Status = m.Status,
                DurationSeconds = (long)m.DurationSeconds,
                StartTime = (long)m.StartTimeDerived,
                Fuels = m.Fuel.Select(f => new SpaceMissionFuel {
                    Amount = f.Amount,
                    Egg = f.Egg
                }).ToList(),
                Targeting = (int)m.Ship >= 4 ? m?.TargetArtifact ?? Name.Unknown : Name.Unknown,
                Capacity = m.Capacity,
                Stars = m.Level
            }).ToList();

            var fm = backup.ArtifactsDb?.FuelingMission ?? null;
            if(fm != null) {
                FuelingMission = new SpaceMission {
                    Ship = fm.Ship,
                    Duration = fm.DurationType,
                    Status = fm.Status,
                    DurationSeconds = (long)fm.DurationSeconds,
                    StartTime = (long)fm.StartTimeDerived,
                    Fuels = fm.Fuel.Select(f => new SpaceMissionFuel {
                        Amount = f.Amount,
                        Egg = f.Egg
                    }).ToList(),
                    Targeting = (int)fm.Ship >= 4 ? fm?.TargetArtifact ?? Name.Unknown : Name.Unknown,
                    Capacity = fm.Capacity,
                    Stars = fm.Level
                };
            }

            FuelAmounts = [];
            for(var i = 0; i < backup.Artifacts.TankFuels.Count; i++) {
                if(backup.Artifacts.TankFuels[i] > 0)
                    FuelAmounts.Add((Ei.Egg)(i + 1), backup.Artifacts.TankFuels[i]);
            }

            MaxFarmSizeReached = [];
            for(var i = 0; i < backup.Game.MaxFarmSizeReached.Count; i++) {
                if(backup.Game.MaxFarmSizeReached[i] > 0)
                    MaxFarmSizeReached.Add((Ei.Egg)(i + 1), backup.Game.MaxFarmSizeReached[i]);
            }

            CustomEggMaxFarmSizeReached = [];
            foreach(var customEgg in backup.Contracts.CustomEggInfo.ToList()) {
                var allContractList = backup.Contracts.Archive;
                allContractList.AddRange(backup.Contracts.Contracts);
                var matchingContracts = allContractList.Where(f =>
                    f?.MaxFarmSizeReached > 0
                    && f.Contract.Egg == Ei.Egg.CustomEgg
                    && f.Contract.CustomEggId.ToLower() == customEgg.Identifier.ToLower()
                ).ToList();

                if(!matchingContracts.Any()) continue;

                CustomEggMaxFarmSizeReached.Add(
                    customEgg.Identifier,
                    (ulong)matchingContracts.Max(f => f.MaxFarmSizeReached)
                );
            }


            var temp = backup.ArtifactsDb.MissionArchive.Where(x => x.DurationSeconds > 0).GroupBy(x => x.Ship);
            if(backup.ArtifactsDb is not null) {
                ShipsSent = backup.ArtifactsDb.MissionArchive.Where(x => x.DurationSeconds > 0).GroupBy(x => new { x.Ship, x.DurationType }).Select(x => (x.Key.Ship, x.Key.DurationType, x.Count())).ToList();
                foreach(var ship in backup.ArtifactsDb.MissionInfos.Where(x => (int)x.Status > 5)) {
                    var shipInfo = ShipsSent.FirstOrDefault(x => x.ship == ship.Ship && x.type == ship.DurationType);
                    if(shipInfo != default) {
                        shipInfo.count++;
                        ShipsSent.RemoveAll(x => x.ship == ship.Ship && x.type == ship.DurationType);
                        ShipsSent.Add(shipInfo);
                    } else {
                        ShipsSent.Add((ship.Ship, ship.DurationType, 1));
                    }
                }
            }



            NumDailyGiftsCollected = backup.Game.NumDailyGiftsCollected;

            EggMedalLevel = backup.Game.EggMedalLevel.ToList();

            ArtifactHall = backup.ArtifactsDb.InventoryItems.Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact is not null) {
                    artifact.Stones = x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null).ToList();
                }
                var artifactStatus = backup.ArtifactsDb.ArtifactStatus.FirstOrDefault(a =>
                    a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                );
                return new ArtifactCount { Count = (int)x.Quantity, Artifact = artifact, NumberCrafted = artifactStatus?.Count ?? 0 };
            }).ToList();

            /* Setup for artifact sets */
            var afxSetsItemIds = backup.ArtifactsDb.SavedArtifactSets.Select(s => {
                return s.Slots.Select(sl => sl.ItemId).ToList();
            }).ToList();

            List<List<EggIncArtifactInstance>> afxSetsList = new();
            foreach(var afxSetItems in afxSetsItemIds) {
                var afxInstances = new List<EggIncArtifactInstance>();
                foreach(var id in afxSetItems) {
                    var x = backup.ArtifactsDb.InventoryItems.FirstOrDefault(item => item.ItemId == id);
                    if(x is null) continue;

                    var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                    if(artifact is null) continue;

                    artifact.Stones = x.Artifact.Stones.Select(EggIncArtifacts.GetArtifact).Where(y => y != null).ToList();

                    afxInstances.Add(artifact);
                }
                if(afxInstances.Count == afxSetItems.Count) afxSetsList.Add(afxInstances);
            }
            ArtifactSets = afxSetsList;

            ArtifactHall.AddRange(backup.ArtifactsDb.ArtifactStatus.Where(a =>
                !backup.ArtifactsDb.InventoryItems.Any(x => a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                )
            ).Select(a => new ArtifactCount { Count = 0, Artifact = EggIncArtifacts.GetArtifact(a.Spec), NumberCrafted = a.Count }));
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
                Completed = contract != null ? contract.NumGoalsAchieved == contract.Contract.GetGoals(contract).Count : false,
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
                LastStepTime = (float)farm.LastStepTime,
                //ReportedUUIDs = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier).SelectMany(x => x.Contributors.Where(y => y.UserId == backup.UserId).Select(y => y.Uuid)).ToList(), //  contract?.ReportedUuids.ToList(),
                Grade = contract?.Grade ?? PlayerGrade.GradeUnset,
                EvaluationCxp = (contract?.Evaluation == null ? 0.0 : (float)contract.Evaluation.Cxp),
                ContributionFinalized = contract?.CoopContributionFinalized ?? false,
                CoopSimulationEndTime = contract?.CoopSimulationEndTime ?? 0,
                NumGoalsAchieved = (byte?)contract?.NumGoalsAchieved ?? (byte)0,
            };

            var coops = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier);

            var uuids = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier).SelectMany(x => x.Contributors.Where(y => y.UserId == backup.EiUserId).Select(y => y.Uuid)).ToList();

            customFarm.ReportedUUIDs = uuids;


            customFarm.Artifacts = new List<EggIncArtifactInstance>();
            var farmIndex = backup.Farms.IndexOf(farm);
            if(backup.ArtifactsDb != null) {
                var activeArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets.Count - 1 < farmIndex ? new List<Ei.ArtifactsDB.Types.ActiveArtifactSlot>() : backup.ArtifactsDb.ActiveArtifactSets[farmIndex].Slots.Where(x => x.Occupied);
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

        public uint GetColleggtibleLevel(DBCustomEgg customEgg) {
            return GetColleggtibleLevel(customEgg.Identifier);
        }

        public uint GetColleggtibleLevel(string identifier) {
            if(CustomEggMaxFarmSizeReached.TryGetValue(identifier.ToLower(), out var farmSize)) {
                return farmSize switch {
                    > 10000000000 => 4,
                    > 1000000000 => 3,
                    > 100000000 => 2,
                    > 10000000 => 1,
                    _ => 0
                };
            } else return 0;
        }

        private void AddContracts(RepeatedField<Ei.LocalContract> contracts) {
            foreach(var contract in contracts) {
                ArchivedFarms.Add(new CustomArchivedFarms(contract));
            }
        }

        [IgnoreMember]
        public double SoulEggBonus { get { return EpicResearch is null ? 0 : (double)(EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10; } }
        [IgnoreMember]
        public double ProphecyEggBonus { get { return EpicResearch is null ? 0 : ((double)(EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1; } }
        [IgnoreMember]
        public double EarningsBonus { get { return SoulEggs * SoulEggBonus * Math.Pow(ProphecyEggBonus, EggsOfProphecy); } }

        [IgnoreMember]
        public double MER { get {
                double seQ = SoulEggs / 1e18; // Convert to quintillions
                return Math.Round((91 * Math.Log10(seQ) + 200 - EggsOfProphecy) / 10, 2);
            }
        }
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
        [Key(34)]
        public List<string> ReportedUUIDs { get; set; }
        [Key(35)]
        public PlayerGrade Grade { get; set; }
        [Key(36)]
        public double EvaluationCxp { get; set; }
        [Key(37)]
        public bool ContributionFinalized { get; set; }
        [Key(38)]
        public double CoopSimulationEndTime { get; set; }
        [Key(39)]
        public byte NumGoalsAchieved { get; set; }

        [IgnoreMember]
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }

        private CustomFarmStats _stats = null;
        public CustomFarmStats WithStats(CustomBackup backup, Coop coop, List<DBCustomEgg> customEggs, double? ignoreBuff = null, Contract contract = null) {
            if(_stats == null) {
                var eggLayingBuff = 1.0;
                if(coop != null && coop.LastStatusUpdate is not null) {
                    eggLayingBuff = coop.LastStatusUpdate.Participants.Where(x => x.BuffHistory.Any())
                        .Sum(x => x.BuffHistory.Last().EggLayingRate - 1);
                    ignoreBuff = ignoreBuff ?? (Artifacts.FirstOrDefault(x => x.Boost == EggIncBoostTypeEnum.CoopMembersEggLayingRates)?.Value ?? 1) - 1;
                    if(ignoreBuff.HasValue) {
                        eggLayingBuff -= ignoreBuff.Value;
                    }
                    eggLayingBuff += 1;
                }

                var shipCapPerc = 1.0;
                var eggLayRatePerc = 1.0;
                if(coop is not null && (coop.Contract is not null || contract is not null) && coop.League > 1) {
                    //Very uncommon, but contracts may have nerfs/buffs associated with them
                    var modifiers = (coop.Contract ?? contract).Details.GradeSpecs[(int)coop.League - 1].Modifiers;
                    var eggLayRateMod = modifiers.FirstOrDefault(x => x.Dimension == GameDimension.EggLayingRate);
                    eggLayRatePerc = eggLayRateMod is not null ? (double)eggLayRateMod.Value : 1.0;

                    var shipCapMod = modifiers.FirstOrDefault(x => x.Dimension == GameDimension.ShippingCapacity);
                    shipCapPerc = shipCapMod is not null ? (double)shipCapMod.Value : 1.0;
                }

                var eggLayingResearch = Research.GetEggLayingRatePerSec(this, backup.EpicResearch);
                var eggLayingArtifact = EggIncArtifacts.GetEggLayingRateMultiple(this);

                _stats = new CustomFarmStats();
                _stats.MaxShippingRate = Research.GetShippingCapacityPerSec(this, backup.EpicResearch) * EggIncArtifacts.GetShippingMultiple(this) * shipCapPerc;
                _stats.EggLayingRate = eggLayingResearch * eggLayingArtifact * eggLayingBuff * eggLayRatePerc;
                _stats.CurrentShippingRate = Math.Min(_stats.MaxShippingRate, _stats.EggLayingRate);
                _stats.EggValue = Research.GetEggValue(this, backup.EpicResearch, contract, customEggs) * EggIncArtifacts.GetEggValueMutiple(this);
                _stats.Income = _stats.CurrentShippingRate * _stats.EggValue * (backup.EarningsBonus / 100) * backup.CurrentMultiplier;
                _stats.MaxRunningBonus = Research.MaxRunningBonus(this, backup.EpicResearch) + EggIncArtifacts.GetMaxRunningBonusAdditive(this);
                _stats.HabSpace = Research.GetHabSpace(this, backup.EpicResearch) * Math.Round(EggIncArtifacts.GetHabSpaceMultiple(this), 5);
                _stats.InternalHatchery = (int)(Research.InternalHatchery(this, backup.EpicResearch) * EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.InternalHatchery, this));
            }
            return _stats;
        }
    }

    public class CustomFarmStats {
        public double CurrentShippingRate { get; set; }
        public double EggLayingRate { get; set; }
        public double MaxShippingRate { get; set; }
        public double EggValue { get; set; }
        public double Income { get; set; }
        public double MaxRunningBonus { get; set; }
        public double HabSpace { get; set; }
        public int InternalHatchery { get; set; }
    }

    [MessagePackObject]
    public class CustomArchivedFarms {
        [Key(0)]
        public string CoopId { get; set; }
        [Key(1)]
        public string ContractId { get; set; }
        [Key(2)]
        public float TimeAccepted { get; set; }
        [Key(3)]
        public bool Completed { get; set; }
        [Key(4)]
        public byte? League { get; set; }
        [Key(5)]
        public byte PEPossible { get; set; }
        [Key(6)]
        public byte PEGained { get; set; }
        [Key(7)]
        public float ContributionAmount { get; set; }
        [Key(8)]
        public PlayerGrade Grade { get; set; }
        [Key(9)]
        public float EvaluationCxp { get; set; }
        [Key(10)]
        public byte NumGoalsAchieved { get; set; }
        [Key(11)]
        public List<string> ReportedUUIDs { get; set; }

        [IgnoreMember]
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }

        public CustomArchivedFarms() { }
        public CustomArchivedFarms(Ei.LocalContract localContract) {
            CoopId = localContract.CoopIdentifier;
            ContractId = localContract.Contract.Identifier;
            TimeAccepted = (float)localContract.TimeAccepted;
            Completed = localContract.Completed;
            League = (byte)localContract.League;
            ContributionAmount = (float)localContract.CoopLastUploadedContribution;
            Grade = localContract.Grade;
            
            if(localContract.Evaluation != null) {
                EvaluationCxp = ((float?)localContract?.Evaluation?.Cxp) ?? 0.0f;
            }
            var goals = localContract.Contract.Goals;
            if(localContract.Contract.GoalSets is not null && localContract.Contract.GoalSets.Count > localContract.League)
                goals = localContract.Contract.GoalSets[(int)localContract.League].Goals;
            if(localContract.Contract.GradeSpecs is not null && localContract.Contract.GradeSpecs.Count > 0 && localContract.Grade > 0)
                goals = localContract.Contract.GradeSpecs[(int)localContract.Grade - 1].Goals;
            PEPossible += (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy).Sum(x => x.RewardAmount);
            PEGained += (byte)goals.Where(x => x.RewardType == Ei.RewardType.EggsOfProphecy && goals.IndexOf(x) < localContract.NumGoalsAchieved).Sum(x => x.RewardAmount);
            NumGoalsAchieved = (byte)localContract.NumGoalsAchieved;
            ReportedUUIDs = localContract.ReportedUuids.ToList();
        }
    }


    public class CustomUniversalFarm {
        public static implicit operator CustomUniversalFarm(CustomFarm farm) {
            return new CustomUniversalFarm {
                FarmType = farm.FarmType, Artifacts = farm.Artifacts, BoostsUsed = farm.BoostsUsed, BoostTokensGiven = farm.BoostTokensGiven, BoostTokensReceived = farm.BoostTokensReceived,
                BoostTokensSpent = farm.BoostTokensSpent, Cancelled = farm.Cancelled, CashEarned = farm.CashEarned, CashSpent = farm.CashSpent, CommonResearch = farm.CommonResearch, Completed = farm.Completed, ContractId = farm.ContractId, ContributionFinalized = farm.ContributionFinalized, CoopAllowed = farm.CoopAllowed,
                CoopId = farm.CoopId, CoopSharedEndTime = farm.CoopSharedEndTime, EggsPaidFor = farm.EggsPaidFor, EggType = farm.EggType, EvaluationCxp = farm.EvaluationCxp, Grade = farm.Grade, Habs = farm.Habs, LastStepTime = farm.LastStepTime, League = farm.League, NumChickens = farm.NumChickens,
                ReportedUUIDs = farm.ReportedUUIDs, SilosOwned = farm.SilosOwned, TimeAccepted = farm.TimeAccepted, TimeCheatDebt = farm.TimeCheatDebt, TimeCheatsDetected = farm.TimeCheatsDetected, TrainLength = farm.TrainLength
            };
        }
        public static implicit operator CustomUniversalFarm(CustomArchivedFarms farm) {
            return new CustomUniversalFarm {
                CoopId = farm.CoopId, ContractId = farm.ContractId, TimeAccepted = (long)farm.TimeAccepted, Completed = farm.Completed, League = farm.League, ContributionAmount = farm.ContributionAmount,
                Grade = farm.Grade, EvaluationCxp = farm.EvaluationCxp, PEGained = farm.PEGained, PEPossible = farm.PEPossible
            };
        }

        public Ei.FarmType FarmType { get; set; }
        public string ContractId { get; set; }
        public double EggsPaidFor { get; set; }
        public uint? League { get; set; }
        public string CoopId { get; set; }
        public bool Cancelled { get; set; }
        public bool Completed { get; set; }
        public List<CustomResearch> CommonResearch { get; set; }
        public ulong NumChickens { get; set; }
        public Ei.Egg EggType { get; set; }
        public List<uint> TrainLength { get; set; }
        public List<uint> Vehicles;
        public List<EggIncArtifactInstance> Artifacts { get; set; }
        public uint SilosOwned { get; set; }
        public long TimeAccepted { get; set; }
        public bool CoopAllowed { get; set; }
        public long CoopSharedEndTime { get; set; }
        public ushort BoostTokensReceived { get; set; }
        public ushort BoostTokensGiven { get; set; }
        public ushort BoostTokensSpent { get; set; }
        public double CashEarned { get; set; }
        public double CashSpent { get; set; }
        public long TimeCheatDebt { get; set; }
        public ushort BoostsUsed { get; set; }
        public ushort TimeCheatsDetected { get; set; }
        public List<ushort> Habs { get; set; }
        public float LastStepTime { get; set; }
        public List<string> ReportedUUIDs { get; set; }
        public PlayerGrade Grade { get; set; }
        public double EvaluationCxp { get; set; }
        public bool ContributionFinalized { get; set; }
        public uint PEPossible { get; set; }
        public uint PEGained { get; set; }
        public double ContributionAmount { get; set; }
    }

    [MessagePackObject]
    public class SpaceMission {
        [Key(0)]
        public Spaceship Ship { get; set; }
        [Key(1)]
        public DurationType Duration { get; set; }
        [Key(2)]
        public Status Status { get; set; }
        [Key(3)]
        public List<SpaceMissionFuel> Fuels { get; set; }
        [Key(4)]
        public long DurationSeconds { get; set; }
        [Key(5)]
        public long StartTime { get; set; }
        [Key(6)]
        public Name Targeting { get; set; } = Name.Unknown;
        [Key(7)]
        public uint Capacity { get; set; } = 0;
        [Key(8)]
        public uint Stars { get; set; } = 0;


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
