using Ei;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static Ei.Backup.Types;

namespace EGG9000.Common.Helpers {
    public class ArtifactHelpers {

        public static string GetArtifactFairnessScoreString(List<ArtifactCount> ArtifactHall) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) return "0 (null artifact hall)";
            else return GetArtifactFairnessScore(ArtifactHall).ToString("E");
        }

        public static BigInteger GetArtifactFairnessScore(List<ArtifactCount> ArtifactHall) {
            if(ArtifactHall is null || ArtifactHall.Count == 0) return 0;
            else {
                BigInteger score = 0;
                foreach(var aCount in ArtifactHall) {
                    score += (BigInteger)(Math.Pow(GetFairness(aCount.Artifact)[aCount.Artifact.Tier - 1], aCount.Artifact.Rarity + 1) * aCount.Count);
                }
                return score;
            }
        }

        public static long[] GetFairness(EggIncArtifactInstance instance) {
            return instance.Artifact switch {
                "Aurelian Brooch" => new long[] { 0, 1186, 13827, 58753 },
                "Beak of Midas" => new long[] { 0, 6075, 23885, 86083 },
                "Book of Basan" => new long[] { 0, 114405, 360427, 934701 },
                "Carved Rainstick" => new long[] { 0, 10082, 39572, 168121 },
                "Clarity Stone" => new long[] { 0, 36603, 185026, 774986 },
                "Demeters Necklace" => new long[] { 0, 383, 7628, 41267 },
                "Dilithium Monocle" => new long[] { 0, 24487, 80590, 271970 },
                "Dilithium Stone" => new long[] { 0, 19671, 82572, 459362 },
                "Gusset" => new long[] { 0, 5167, 28986, 163774 },
                "Interstellar Compass" => new long[] { 0, 9425, 37923, 226570 },
                "Life Stone" => new long[] { 0, 10507, 63177, 325027 },
                "Light of Eggendil" => new long[] { 0, 67892, 168121, 330068 },
                "Lunar Stone" => new long[] { 0, 406, 12447, 155125 },
                "Lunar Totem" => new long[] { 1, 1, 4749, 28986 },
                "Mercury's Lens" => new long[] { 0, 7084, 30365, 295510 },
                "Neodymium Medallion" => new long[] { 0, 3982, 16466, 58753 },
                "Phoenix Feather" => new long[] { 0, 16466, 50473, 237296 },
                "Prophecy Stone" => new long[] { 0, 50259, 246536, 731674 },
                "Puzzle Cube" => new long[] { 0, 90, 14672, 110353 },
                "Quantum Metronome" => new long[] { 0, 18400, 67892, 306621 },
                "Quantum Stone" => new long[] { 0, 7252, 25118, 228278 },
                "Shell Stone" => new long[] { 0, 262, 18048, 183125 },
                "Ship in a Bottle" => new long[] { 0, 36322, 113895, 442510 },
                "Soul Stone" => new long[] { 0, 5196, 64770, 250022 },
                "Tachyon Deflector" => new long[] { 0, 77412, 259516, 1220591 },
                "Tachyon Stone" => new long[] { 0, 495, 18048, 236946 },
                "Terra Stone" => new long[] { 0, 5196, 29988, 303837 },
                "The Chalice" => new long[] { 0, 8798, 30365, 139253 },
                "Titanium Actuator" => new long[] { 0, 26353, 88920, 215900 },
                "Tungsten Ankh" => new long[] { 0, 3982, 25099, 110546 },
                "Vial of Martian Dust" => new long[] { 0, 3302, 26353, 139253 },
                _ => new long[] { 0, 0, 0, 0 }
            };
        }

        public static uint GetTotalCraftWithLegendaryPossibility(List<ArtifactCount> artifactHall) {
            if(artifactHall is null || artifactHall.Count == 0) return 0;

            uint totalCrafts = 0;

            foreach(var artifactCount in artifactHall) {
                var artifactName = artifactCount.Artifact.Artifact;

                if(artifactCount.Artifact.Tier == 4 && artifactName != "Lunar Totem") {
                    totalCrafts += artifactCount.NumberCrafted;
                } else if(artifactCount.Artifact.Tier == 3 && artifactName == "Tungsten Ankh") {
                    totalCrafts += artifactCount.NumberCrafted;
                }
            }
            return totalCrafts;
        }

        public static int GetLegendaryArtifactCount(List<ArtifactCount> artifactHall) {
            int legendaryCount = 0;

            foreach (var artifactCount in artifactHall) {
                if(artifactCount.Artifact.Rarity == 4) {
                    legendaryCount += artifactCount.Count;
                }
            }
            return legendaryCount;
        }
    }
}
