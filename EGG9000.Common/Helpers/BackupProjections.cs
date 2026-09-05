using EGG9000.Common.Database;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using static Ei.ArtifactSpec.Types;
using static Ei.MissionInfo.Types;

namespace EGG9000.Common.Helpers {
    public static class BackupProjections {
        public static Dictionary<Ei.Egg, ulong> BuildMaxFarmSizeReached(Ei.Backup.Types.Game game) {
            var sizes = new Dictionary<Ei.Egg, ulong>();
            for(var i = 0; i < game.MaxFarmSizeReached.Count; i++) {
                if(game.MaxFarmSizeReached[i] > 0)
                    sizes.Add((Ei.Egg)(i + 1), game.MaxFarmSizeReached[i]);
            }
            return sizes;
        }

        public static List<EggIncArtifactInstance> ResolveActiveArtifacts(IEnumerable<Ei.ArtifactInventoryItem> activeArtifacts) {
            return [.. activeArtifacts.Where(x => x != null).Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact == null)
                    return null;
                artifact.Stones = [.. x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null)];
                return artifact;
            }).Where(x => x != null)];
        }

        public static SpaceMission ToSpaceMission(Ei.MissionInfo m) {
            return new SpaceMission {
                Ship = m.Ship,
                Duration = m.DurationType,
                Status = m.Status,
                DurationSeconds = (long)m.DurationSeconds,
                StartTime = (long)m.StartTimeDerived,
                Fuels = [.. m.Fuel.Select(f => new SpaceMissionFuel {
                    Amount = f.Amount,
                    Egg = f.Egg
                })],
                Targeting = (int)m.Ship >= 4 ? m?.TargetArtifact ?? Name.Unknown : Name.Unknown,
                Capacity = m.Capacity,
                Stars = m.Level
            };
        }

        public static Dictionary<Ei.Egg, double> BuildFuelAmounts(Ei.Backup.Types.Artifacts activeTankArtifacts) {
            var fuelAmounts = new Dictionary<Ei.Egg, double>();
            for(var i = 0; i < activeTankArtifacts.TankFuels.Count; i++) {
                if(activeTankArtifacts.TankFuels[i] > 0)
                    fuelAmounts.Add((Ei.Egg)(i + 1), activeTankArtifacts.TankFuels[i]);
            }
            return fuelAmounts;
        }

        public static List<(Spaceship ship, DurationType type, int count)> BuildShipsSent(Ei.ArtifactsDB artifactsDb) {
            List<(Spaceship ship, DurationType type, int count)> shipsSent = [.. artifactsDb.MissionArchive.Where(x => x.DurationSeconds > 0).GroupBy(x => new { x.Ship, x.DurationType }).Select(x => (x.Key.Ship, x.Key.DurationType, x.Count()))];
            foreach(var ship in artifactsDb.MissionInfos.Where(x => (int)x.Status > 5)) {
                var shipInfo = shipsSent.FirstOrDefault(x => x.ship == ship.Ship && x.type == ship.DurationType);
                if(shipInfo != default) {
                    shipInfo.count++;
                    shipsSent.RemoveAll(x => x.ship == ship.Ship && x.type == ship.DurationType);
                    shipsSent.Add(shipInfo);
                } else {
                    shipsSent.Add((ship.Ship, ship.DurationType, 1));
                }
            }
            return shipsSent;
        }

        public static List<ArtifactCount> BuildArtifactHall(Ei.ArtifactsDB artifactsDb) {
            List<ArtifactCount> artifactHall = [.. artifactsDb.InventoryItems.Select(x => {
                var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                if(artifact is not null) {
                    artifact.Stones = [.. x.Artifact.Stones.Select(y => EggIncArtifacts.GetArtifact(y)).Where(y => y != null)];
                }
                var artifactStatus = artifactsDb.ArtifactStatus.FirstOrDefault(a =>
                    a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                );
                return new ArtifactCount { Count = (int)x.Quantity, Artifact = artifact, NumberCrafted = artifactStatus?.Count ?? 0 };
            })];

            artifactHall.AddRange(artifactsDb.ArtifactStatus.Where(a =>
                !artifactsDb.InventoryItems.Any(x => a.Spec.Name == x.Artifact.Spec.Name &&
                    a.Spec.Level == x.Artifact.Spec.Level &&
                    a.Spec.Rarity == x.Artifact.Spec.Rarity
                )
            ).Select(a => new ArtifactCount { Count = 0, Artifact = EggIncArtifacts.GetArtifact(a.Spec), NumberCrafted = a.Count }));

            return artifactHall;
        }

        public static List<List<EggIncArtifactInstance>> BuildArtifactSets(Ei.ArtifactsDB artifactsDb) {
            var afxSetsProjected = artifactsDb.SavedArtifactSets.Select(s =>
                s.Slots.Select(sl => {
                    var x = artifactsDb.InventoryItems.FirstOrDefault(item => item.ItemId == sl.ItemId);
                    if(x is null) return null;
                    var artifact = EggIncArtifacts.GetArtifact(x.Artifact.Spec);
                    if(artifact is null) return null;
                    artifact.Stones = [.. x.Artifact.Stones.Select(EggIncArtifacts.GetArtifact).Where(y => y != null)];
                    return artifact;
                })
            );
            return Helpers.AfxSets.AfxSetsBuilder.BuildSetsPreservingEmpty(afxSetsProjected);
        }

        public static Ei.Backup.Types.Artifacts ResolveActiveTankArtifacts(Ei.Backup backup) {
            var currentFarm = backup.Farms.ElementAtOrDefault((int)backup.Game.CurrentFarm);
            var inVirtueDimension = currentFarm is not null && (int)currentFarm.EggType >= 50 && (int)currentFarm.EggType <= 54;
            return inVirtueDimension && backup.Virtue?.Afx is not null ? backup.Virtue.Afx : backup.Artifacts;
        }

        public static void MergeMaxFarmSizes(Dictionary<string, ulong> target, Dictionary<string, ulong> source) {
            foreach(var kvp in source)
                if(!target.TryGetValue(kvp.Key, out var existing) || kvp.Value > existing)
                    target[kvp.Key] = kvp.Value;
        }

        public static void MergeMaxFarmSizes(Dictionary<string, ulong> target, IEnumerable<Ei.LocalContract> farms, FrozenSet<Ei.Contract> contracts) {
            var eggIdByContractId = contracts
                .Where(c => c != null && c.Egg == Ei.Egg.CustomEgg && !string.IsNullOrEmpty(c.CustomEggId))
                .GroupBy(c => c.Identifier)
                .ToDictionary(g => g.Key, g => g.First().CustomEggId.ToLower());
            MergeMaxFarmSizes(target,
                farms.Where(f => f.MaxFarmSizeReached > 0 && eggIdByContractId.ContainsKey(f.ContractIdentifier))
                     .GroupBy(f => eggIdByContractId[f.ContractIdentifier])
                     .ToDictionary(g => g.Key, g => (ulong)g.Max(f => f.MaxFarmSizeReached)));
        }
    }
}
