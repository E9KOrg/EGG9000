using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiStatics;
using Google.Protobuf.Collections;
using MessagePack;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using static Ei.ArtifactSpec.Types;
using static Ei.Contract.Types;
using static Ei.GameModifier.Types;
using static Ei.MissionInfo.Types;

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

        // Grade of the most recently accepted contract, with when it was accepted. last_cpi is no
        // longer in backups, so this is how we read a player's current grade. The accept time lets
        // callers ignore it when a known promotion is newer than any contract.
        public (Ei.Contract.Types.PlayerGrade Grade, DateTimeOffset Accepted) GetMostRecentContractGrade() {
            var graded = new List<(double time, Ei.Contract.Types.PlayerGrade grade)>();
            if(Farms is not null)
                graded.AddRange(Farms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((double)x.TimeAccepted, x.Grade)));
            if(ArchivedFarms is not null)
                graded.AddRange(ArchivedFarms.Where(x => x.Grade != Ei.Contract.Types.PlayerGrade.GradeUnset).Select(x => ((double)x.TimeAccepted, x.Grade)));
            var latest = graded.OrderByDescending(x => x.time).FirstOrDefault();
            if(latest.grade == Ei.Contract.Types.PlayerGrade.GradeUnset)
                return (Ei.Contract.Types.PlayerGrade.GradeUnset, DateTimeOffset.MinValue);
            return (latest.grade, DateTimeOffset.FromUnixTimeSeconds((long)latest.time));
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
        //[Key(27)]
        //public PlayerGrade Grade { get; set; }
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


        //[Key(43)]
        //public uint EoV { get; set; } = 0;

        [Key(44)]
        public double[] VirtueEggsDelivered { get; set; }
        [Key(45)]
        public uint Resets { get; set; }
        [Key(46)]
        public uint ShiftCount { get; set; }
        [Key(47)]
        public uint[] EovEarned { get; set; }
        [Key(48)]
        public double SubscriptionEnds { get; set; } = 0;
        [Key(49)]
        public Ei.UserSubscriptionInfo.Types.Level? SubscriptionLevel { get; set; } = null;


        [IgnoreMember]
        public uint EggsOfTruth {get { return (uint?)EovEarned?.Sum(x => x) ?? (uint)0; } }

        [IgnoreMember]
        public int EggsOfTruthTotal {  get { return VirtueEggsDelivered?.Select(x => VirtueHelper.CurrentLevel(x)).Sum() ?? 0; } }

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
            Farms?.Where(x => !x.isVirtueEgg).ToList().ForEach(f => f.Artifacts?.ForEach(a => artifacts.FirstOrDefault(x => x.Artifact.Equals(a)).Count--));
            return artifacts?.Where(x => x.Count > 0).ToList() ?? [];
        }

        public List<ArtifactCount> GetAvailableArtifacts(CustomFarm farm) {
            if(ArtifactHall is null || ArtifactHall.Count == 0 || farm.isVirtueEgg) {
                return [];
            }

            var artifacts = ArtifactHall.Select(x => new ArtifactCount { Count = x.Count, Artifact = x.Artifact, NumberCrafted = x.NumberCrafted }).ToList();
            Farms.Where(x => x != farm && x.FarmType != Ei.FarmType.Empty && x.CoopSimulationEndTime == 0).ToList()?.ForEach(f => f.Artifacts?.ForEach(a => { var artifact = artifacts.FirstOrDefault(x => x.Artifact.Equals(a)); if(artifact is not null) artifact.Count--; } ));
            return artifacts?.Where(x => x.Count > 0).ToList() ?? [];
        }

        public CustomBackup() { }

        // CS is sourced out-of-band (get_contract_player_info), so the protobuf rebuild has no fresh
        // value. Keep the prior value unless a positive fresh one is supplied. -1 is the legacy
        // "unknown" sentinel and counts as no value.
        public static double CarryForwardCs(double fresh, double last) => fresh > 0 ? fresh : last;

        public CustomBackup(Ei.Backup backup, FrozenSet<Ei.Contract> contracts, CustomBackup lastBackup = null) {
            if(backup?.Game == null) {
                EmptyBackup = true;
                return;
            }
            EpicResearch = backup.Game.EpicResearch.Select(x => new CustomResearch(x)).ToList();
            CurrentMultiplier = backup.Game.CurrentMultiplier;
            EggIncId = backup.GetID();
            UserName = string.IsNullOrEmpty(backup.UserName) ? lastBackup?.UserName ?? "" : backup.UserName;
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
            var currentFarm = backup.Farms.ElementAtOrDefault((int)backup.Game.CurrentFarm);
            var inVirtueDimension = currentFarm is not null && (int)currentFarm.EggType >= 50 && (int)currentFarm.EggType <= 54;
            var activeTankArtifacts = inVirtueDimension && backup.Virtue?.Afx is not null ? backup.Virtue.Afx : backup.Artifacts;
            TankLevel = activeTankArtifacts.TankLevel;
            //GradeProgress = backup.Contracts.LastCpi?.GradeProgress ?? 0;
            ClientVersion = (byte)backup.Version;

            // CS is written out-of-band by AccountRefresh.ApplyExtrasAsync (from get_contract_player_info),
            // not derived from this protobuf backup. Carry the last known value forward so a mass-backup
            // rebuild doesn't reset it to 0 and drop the user from CSLeaderboard's "TotalCS > 0" filter.
            TotalCS = CarryForwardCs(0, lastBackup?.TotalCS ?? 0);
            SeasonCS = CarryForwardCs(0, lastBackup?.SeasonCS ?? 0);

            VirtueEggsDelivered = backup.Virtue?.EggsDelivered.ToArray() ?? Array.Empty<double>();
            Resets = backup.Virtue?.Resets ?? 0;
            ShiftCount = backup.Virtue?.ShiftCount ?? 0;
            EovEarned = backup.Virtue?.EovEarned.ToArray() ?? Array.Empty<uint>();

            SetSubscriptionInfo(backup);

            HasDeviceId = backup.HasDeviceId;
            if(backup.HasDeviceId) DeviceId = backup.DeviceId;

            MaxEggReached = backup.Game.MaxEggReached;

            CraftingXP = backup.Artifacts.CraftingXp;

            ArchivedFarms = new List<CustomArchivedFarms>();
            AddContracts(backup.Contracts.Contracts, contracts);
            AddContracts(backup.Contracts.Archive, contracts);

            Farms = new List<CustomFarm>();
            foreach(var farm in backup.Farms.Where(x => x.FarmType != Ei.FarmType.Empty)) {
                AddFarm(farm, backup);
            }



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
            for(var i = 0; i < activeTankArtifacts.TankFuels.Count; i++) {
                if(activeTankArtifacts.TankFuels[i] > 0)
                    FuelAmounts.Add((Ei.Egg)(i + 1), activeTankArtifacts.TankFuels[i]);
            }

            MaxFarmSizeReached = [];
            for(var i = 0; i < backup.Game.MaxFarmSizeReached.Count; i++) {
                if(backup.Game.MaxFarmSizeReached[i] > 0)
                    MaxFarmSizeReached.Add((Ei.Egg)(i + 1), backup.Game.MaxFarmSizeReached[i]);
            }

            CustomEggMaxFarmSizeReached = [];
            MergeMaxFarmSizes(CustomEggMaxFarmSizeReached, backup.Contracts.Archive.Concat(backup.Contracts.Contracts), contracts);
            if(lastBackup?.CustomEggMaxFarmSizeReached is not null)
                MergeMaxFarmSizes(CustomEggMaxFarmSizeReached, lastBackup.CustomEggMaxFarmSizeReached);


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
            var afxSetsProjected = backup.ArtifactsDb.SavedArtifactSets.Select(s =>
                s.Slots.Select(sl => {
                    var x = backup.ArtifactsDb.InventoryItems.FirstOrDefault(item => item.ItemId == sl.ItemId);
                    if(x is null) return null;
                    var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                    if(artifact is null) return null;
                    artifact.Stones = x.Artifact.Stones.Select(EggIncArtifacts.GetArtifact).Where(y => y != null).ToList();
                    return artifact;
                })
            );
            ArtifactSets = EGG9000.Common.Helpers.AfxSets.AfxSetsBuilder.BuildSetsPreservingEmpty(afxSetsProjected);

            ArtifactHall.AddRange(backup.ArtifactsDb.ArtifactStatus.Where(a =>
                !backup.ArtifactsDb.InventoryItems.Any(x => a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                )
            ).Select(a => new ArtifactCount { Count = 0, Artifact = EggIncArtifacts.GetArtifact(a.Spec), NumberCrafted = a.Count }));
        }

        private void SetSubscriptionInfo(Ei.Backup backup) {
            var subInfo = backup.SubInfo;
            if(subInfo is null) return;

            var hasActiveStatus = subInfo.HasStatus && (subInfo.Status == Ei.UserSubscriptionInfo.Types.Status.Active || subInfo.Status == Ei.UserSubscriptionInfo.Types.Status.GracePeriod);
            var inSubPeriod = subInfo.PeriodEnd > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(!hasActiveStatus || !inSubPeriod) {
                SubscriptionEnds = subInfo.PeriodEnd;
                return;
            }

            SubscriptionLevel = subInfo.SubscriptionLevel;
            SubscriptionEnds = subInfo.PeriodEnd;
        }

        private void AddFarm(Ei.Backup.Types.Simulation farm, Ei.Backup backup) {
            var contract = backup.Contracts.Contracts.FirstOrDefault(x => x.ContractIdentifier == farm.ContractId)
                ?? backup.Contracts.Archive.Where(x => x != null).FirstOrDefault(x => x.ContractIdentifier == farm.ContractId);

            var customFarm = new CustomFarm {
                FarmType = farm.FarmType,
                ContractId = farm.ContractId,
                EggsPaidFor = farm.EggsPaidFor,
                League = contract?.League,
                CoopId = contract?.CoopIdentifier,
                Cancelled = contract?.Cancelled ?? false,
                Completed = contract?.Contract != null && contract.NumGoalsAchieved == contract.Contract.GetGoals(contract).Count,
                NumChickens = farm.NumChickens,
                CommonResearch = farm.CommonResearch.Select(x => new CustomResearch(x)).ToList(),
                EggType = farm.EggType,
                Vehicles = farm.Vehicles.ToList(),
                TrainLength = farm.TrainLength.ToList(),
                SilosOwned = farm.SilosOwned,
                TimeAccepted = (long)(contract?.TimeAccepted ?? 0),
                CoopAllowed = contract?.Contract?.CoopAllowed ?? false,
                CoopSharedEndTime = (long)(contract?.CoopSharedEndTime ?? 0),
                BoostTokensReceived = (ushort)farm.BoostTokensReceived,
                BoostTokensGiven = (ushort)farm.BoostTokensGiven,
                BoostTokensSpent = (ushort)farm.BoostTokensSpent,
                CashEarned = farm.CashEarned,
                CashSpent = farm.CashSpent,
                TimeCheatDebt = (long)farm.TimeCheatDebtDEP,
                BoostsUsed = (ushort)(contract?.BoostsUsed ?? 0),
                TimeCheatsDetected = (ushort)farm.TimeCheatsDetected,
                Habs = farm.Habs.Select(x => (ushort)x).ToList(),
                LastStepTime = (float)farm.LastStepTime,
                Grade = contract?.Grade ?? PlayerGrade.GradeUnset,
                EvaluationCxp = (contract?.Evaluation == null ? 0.0 : (float)contract.Evaluation.Cxp),
                ContributionFinalized = contract?.CoopContributionFinalized ?? false,
                CoopSimulationEndTime = contract?.CoopSimulationEndTime ?? 0,
                NumGoalsAchieved = (byte?)contract?.NumGoalsAchieved ?? (byte)0,
            };


            var currentCoopStatus = backup.Contracts.CurrentCoopStatuses.Where(x => x.ContractIdentifier == farm.ContractId).FirstOrDefault();
            if(currentCoopStatus != null)
                customFarm.Creator = currentCoopStatus.CreatorId == backup.GetID();


            var coops = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier);

            var uuids = backup.Contracts.CurrentCoopStatuses.Where(x => x.CoopIdentifier == contract?.CoopIdentifier).SelectMany(x => x.Contributors.Where(y => y.UserId == backup.EiUserId).Select(y => y.Uuid)).ToList();

            customFarm.ReportedUUIDs = uuids;


            customFarm.Artifacts = new List<EggIncArtifactInstance>();
            var farmIndex = backup.Farms.IndexOf(farm);
            if(backup.ArtifactsDb != null) {
                if(farmIndex == 0 && (int)farm.EggType >= 50 && (int)farm.EggType <= 54) {
                    //Handle Virtue Eggs

                    var activeArtifactSlots = backup.ArtifactsDb.VirtueAfxDb.ActiveArtifacts.Slots;
                    var activeArtifacts = activeArtifactSlots.Select(x => backup.ArtifactsDb.VirtueAfxDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));

                    customFarm.Artifacts.AddRange(activeArtifacts.Where(x => x != null).Select(x => {
                        var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                        if(artifact == null)
                            return null;
                        artifact.Stones = x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null).ToList();
                        return artifact;
                    }).Where(x => x != null));

                    customFarm.Artifacts = customFarm.Artifacts.Where(x => x != null).ToList();
                } else {
                    var activeArtifactSlots = backup.ArtifactsDb.ActiveArtifactSets.Count - 1 < farmIndex ? new List<Ei.ActiveArtifactSlot>() : backup.ArtifactsDb.ActiveArtifactSets[farmIndex].Slots.Where(x => x.Occupied);
                    var activeArtifacts = activeArtifactSlots.Select(x => backup.ArtifactsDb.InventoryItems.FirstOrDefault(y => y.ItemId == x.ItemId));

                    customFarm.Artifacts.AddRange(activeArtifacts.Where(x => x != null).Select(x => {
                        var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                        if(artifact == null)
                            return null;
                        artifact.Stones = x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null).ToList();
                        return artifact;
                    }).Where(x => x != null));

                    customFarm.Artifacts = customFarm.Artifacts.Where(x => x != null).ToList();
                }
            }



            Farms.Add(customFarm);
        }

        private static void MergeMaxFarmSizes(Dictionary<string, ulong> target, Dictionary<string, ulong> source) {
            foreach(var kvp in source)
                if(!target.TryGetValue(kvp.Key, out var existing) || kvp.Value > existing)
                    target[kvp.Key] = kvp.Value;
        }

        private static void MergeMaxFarmSizes(Dictionary<string, ulong> target, IEnumerable<Ei.LocalContract> farms, FrozenSet<Ei.Contract> contracts) {
            var eggIdByContractId = contracts
                .Where(c => c.Egg == Ei.Egg.CustomEgg && !string.IsNullOrEmpty(c.CustomEggId))
                .ToDictionary(c => c.Identifier, c => c.CustomEggId.ToLower());
            MergeMaxFarmSizes(target,
                farms.Where(f => f.MaxFarmSizeReached > 0 && eggIdByContractId.ContainsKey(f.ContractIdentifier))
                     .GroupBy(f => eggIdByContractId[f.ContractIdentifier])
                     .ToDictionary(g => g.Key, g => (ulong)g.Max(f => f.MaxFarmSizeReached)));
        }

        public uint GetColleggtibleLevel(string identifier) {
            CustomEggMaxFarmSizeReached.TryGetValue(identifier.ToLower(), out var farmSize);
            return LevelForFarmSize(farmSize);
        }

        // Level plus the raw max habitat population reached, in one lookup.
        public (uint Level, ulong FarmSize) GetColleggtibleProgress(string identifier) {
            CustomEggMaxFarmSizeReached.TryGetValue(identifier.ToLower(), out var farmSize);
            return (LevelForFarmSize(farmSize), farmSize);
        }

        private static uint LevelForFarmSize(ulong farmSize) => farmSize switch {
            > 10000000000UL => 4,
            > 1000000000UL => 3,
            > 100000000UL => 2,
            > 10000000UL => 1,
            _ => 0
        };

        private void AddContracts(RepeatedField<Ei.LocalContract> contracts, FrozenSet<Ei.Contract> allContracts) {
            foreach(var localContract in contracts) {
                if(localContract.Contract is null) {
                    var contract = allContracts.FirstOrDefault(x => x.Identifier == localContract.ContractIdentifier);
                    if(contract is null) {
                        // Definition is not in our cache/DB (e.g. a contract never offered to the
                        // reference account, absorbed lazily via get_contracts_info). Skip this entry
                        // instead of crashing the whole backup or attaching an unrelated contract.
                        Console.WriteLine($"Missing contract definition, skipping: {localContract.ContractIdentifier}");
                        continue;
                    }
                    localContract.Contract = contract;
                }
                ArchivedFarms.Add(new CustomArchivedFarms(localContract));
            }
        }

        [IgnoreMember]
        public double SoulEggBonus { get { return EpicResearch is null ? 0 : (double)(EpicResearch.FirstOrDefault(x => x.Id == "soul_eggs")?.Level ?? 0d) + 10; } }
        [IgnoreMember]
        public double ProphecyEggBonus { get { return EpicResearch is null ? 0 : ((double)(EpicResearch.FirstOrDefault(x => x.Id == "prophecy_bonus")?.Level ?? 0d) + 5) / 100 + 1; } }
        [IgnoreMember]
        public double EarningsBonus { get { return SoulEggs * SoulEggBonus * Math.Pow(ProphecyEggBonus, EggsOfProphecy) * (Math.Pow(1.01, EggsOfTruth)); } }

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
        [Key(40)]
        public bool Creator { get; set; }

        [IgnoreMember]
        public DateTimeOffset Started { get { return DateTimeOffset.FromUnixTimeSeconds((long)TimeAccepted); } }

        public class Colleggtible {
            public GameDimension Dimension { get; set; }
            public double Value { get; set; }
        }

        [IgnoreMember]
        public bool isVirtueEgg { get { return (int)EggType >= 50 && (int)EggType <= 54; } }

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

                var dimensionColleggtibleEffect = Colleggtibles.GetCollectibleData(customEggs, backup);

                _stats = new CustomFarmStats {
                    MaxShippingRate = Research.GetShippingCapacityPerSec(this, backup.EpicResearch) * EggIncArtifacts.GetShippingMultiple(this) * shipCapPerc * dimensionColleggtibleEffect[GameDimension.ShippingCapacity],
                    EggLayingRate = eggLayingResearch * eggLayingArtifact * eggLayingBuff * eggLayRatePerc * dimensionColleggtibleEffect[GameDimension.EggLayingRate]
                };
                _stats.CurrentShippingRate = Math.Min(_stats.MaxShippingRate, _stats.EggLayingRate);
                _stats.EggValue = Research.GetEggValue(this, backup.EpicResearch, contract, customEggs) * EggIncArtifacts.GetEggValueMutiple(this);
                _stats.Income = _stats.CurrentShippingRate * _stats.EggValue * (backup.EarningsBonus / 100) * backup.CurrentMultiplier * dimensionColleggtibleEffect[GameDimension.Earnings];
                _stats.MaxRunningBonus = Research.MaxRunningBonus(this, backup.EpicResearch) + EggIncArtifacts.GetMaxRunningBonusAdditive(this);
                _stats.HabSpace = Research.GetHabSpace(this, backup.EpicResearch) * Math.Round(EggIncArtifacts.GetHabSpaceMultiple(this), 5) * dimensionColleggtibleEffect[GameDimension.HabCapacity];
                _stats.InternalHatchery = (int)(Research.InternalHatchery(this, backup.EpicResearch) * EggIncArtifacts.GetMultiple(EggIncBoostTypeEnum.InternalHatchery, this) * dimensionColleggtibleEffect[GameDimension.InternalHatcheryRate]);
                if((int)EggType >= 50 && (int)EggType <= 54) {
                    //Virtue Egg
                    _stats.InternalHatchery = (int)((double)_stats.InternalHatchery * Math.Pow(1.1, backup.EggsOfTruth));
                }
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
