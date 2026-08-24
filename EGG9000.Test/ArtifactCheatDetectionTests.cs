using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ArtifactCheatDetectionTests {
        private static EggIncAccount AccountWithLegendaries(int legendaryCount, double craftingXp = 0) {
            var hall = new List<ArtifactCount>();
            for(var i = 0; i < legendaryCount; i++) {
                hall.Add(new ArtifactCount {
                    Count = 1000,
                    NumberCrafted = 0,
                    Artifact = new EggIncArtifactInstance { Id = 26, Tier = 4, Rarity = 4, Stones = [] }
                });
            }
            return new EggIncAccount {
                Id = "EI0000000000000000",
                Name = "cheater",
                Backup = new CustomBackup {
                    ArtifactHall = hall,
                    CraftingXP = craftingXp
                }
            };
        }

        [TestMethod]
        public void GetLegendaryLuckCoefficient_ZeroBaselineWithLegendaries_DoesNotCollapseToZeroPercent() {
            var account = AccountWithLegendaries(legendaryCount: 5);
            var freshBackup = new Ei.Backup { ArtifactsDb = new Ei.ArtifactsDB() };

            var result = ArtifactHelpers.GetLegendaryLuckCoefficient(account, freshBackup, []);

            Assert.AreEqual(0, result.ExpectedLeggies, "test setup should exercise the zero-expected-leggies branch");
            Assert.AreEqual(ArtifactHelpers.NoBaselineLLCPercent, result.LLCPercent);
            Assert.IsTrue(result.LLCPercent >= ArtifactHelpers.LLCPercentHardCutoff);
        }

        [TestMethod]
        public void GetLegendaryLuckCoefficient_ZeroBaselineNoLegendaries_StaysZeroPercent() {
            var account = AccountWithLegendaries(legendaryCount: 0);
            var freshBackup = new Ei.Backup { ArtifactsDb = new Ei.ArtifactsDB() };

            var result = ArtifactHelpers.GetLegendaryLuckCoefficient(account, freshBackup, []);

            Assert.AreEqual(0, result.ExpectedLeggies);
            Assert.AreEqual(0, result.LLCPercent);
        }

        [TestMethod]
        public void IsCheatFlagged_HardCutoffAlone_Flags() {
            Assert.IsTrue(ArtifactHelpers.IsCheatFlagged(hasLlc: true, llcPercent: ArtifactHelpers.LLCPercentHardCutoff, hasAfs: false, afsZ: 0));
        }

        [TestMethod]
        public void IsCheatFlagged_SoftCutoffWithAfsCorroboration_Flags() {
            Assert.IsTrue(ArtifactHelpers.IsCheatFlagged(hasLlc: true, llcPercent: ArtifactHelpers.LLCPercentSoftCutoff, hasAfs: true, afsZ: ArtifactHelpers.AFSZScoreCutoff + 0.1));
        }

        [TestMethod]
        public void IsCheatFlagged_SoftCutoffWithoutAfs_DoesNotFlag() {
            Assert.IsFalse(ArtifactHelpers.IsCheatFlagged(hasLlc: true, llcPercent: ArtifactHelpers.LLCPercentSoftCutoff, hasAfs: false, afsZ: 0));
        }

        [TestMethod]
        public void IsCheatFlagged_LlcUnavailable_ExtremeAfsAlone_Flags() {
            Assert.IsTrue(ArtifactHelpers.IsCheatFlagged(hasLlc: false, llcPercent: 0, hasAfs: true, afsZ: ArtifactHelpers.AFSZScoreAloneCutoff));
        }

        [TestMethod]
        public void IsCheatFlagged_LlcUnavailable_ModerateAfsAlone_DoesNotFlag() {
            Assert.IsFalse(ArtifactHelpers.IsCheatFlagged(hasLlc: false, llcPercent: 0, hasAfs: true, afsZ: ArtifactHelpers.AFSZScoreAloneCutoff - 0.1));
        }

        [TestMethod]
        public void IsCheatFlagged_NeitherSignal_DoesNotFlag() {
            Assert.IsFalse(ArtifactHelpers.IsCheatFlagged(hasLlc: false, llcPercent: 0, hasAfs: false, afsZ: 0));
        }
    }
}
