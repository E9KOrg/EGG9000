using System.Collections.Generic;

using EGG9000.Common.Helpers.AfxSets;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ArtifactSetsTests {
        private static readonly string[] expectedFirst = ["a", "b"];
        private static readonly string[] expectedSecond = ["c", "d", "e", "f"];

        [TestMethod]
        public void BuildSetsPreservingEmpty_dropsNullsKeepsEmptyAndOrder() {
            var input = new List<List<string>> {
                new() { "a", null!, "b" }, // partial -> [a, b]
                new() { }, // empty saved set -> []
                new() { null! }, // all unresolved -> []
                new() { "c", "d", "e", "f" }
            };
            var result = AfxSetsBuilder.BuildSetsPreservingEmpty(input);
            Assert.HasCount(4, result, "one output set per saved set");
            CollectionAssert.AreEqual(expectedFirst, result[0]);
            Assert.IsEmpty(result[1]);
            Assert.IsEmpty(result[2]);
            CollectionAssert.AreEqual(expectedSecond, result[3]);
        }

        [TestMethod]
        public void AfxSetsHash_isStableAndOrderSensitive() {
            var a = new EggIncArtifactInstance { Id = 26, Tier = 4, Rarity = 4, Stones = [new EggIncArtifactInstance { Id = 1, Tier = 2, Rarity = 0, Stones = [] }] };
            var b = new EggIncArtifactInstance { Id = 8, Tier = 3, Rarity = 2, Stones = [] };
            var set1 = new List<List<EggIncArtifactInstance>> { new() { a, b } };
            var set1Copy = new List<List<EggIncArtifactInstance>> { new() { a, b } };
            var set2 = new List<List<EggIncArtifactInstance>> { new() { b, a } };

            Assert.AreEqual(AfxSetsHash.Compute(set1), AfxSetsHash.Compute(set1Copy), "same data -> same hash");
            Assert.AreNotEqual(AfxSetsHash.Compute(set1), AfxSetsHash.Compute(set2), "reordering -> different hash");
        }

        [TestMethod]
        public void AfxExplorerLink_buildsArtifactAndStoneUrls() {
            // Id 26 = TachyonDeflector, tier 4 -> tachyon-deflector-4
            var deflector = new EggIncArtifactInstance { Id = 26, Tier = 4, Rarity = 4, Stones = [] };
            Assert.AreEqual(
                "https://wasmegg-carpet.netlify.app/artifact-explorer/#/artifact/tachyon-deflector-4/",
                AfxExplorerLink.Url(deflector, isStone: false));

            // Id 1 = TachyonStone, Tier is 0-based; T4 stone has Tier 3 -> +1 -> tachyon-stone-4
            var stone = new EggIncArtifactInstance { Id = 1, Tier = 3, Rarity = 0, Stones = [] };
            Assert.AreEqual(
                "https://wasmegg-carpet.netlify.app/artifact-explorer/#/artifact/tachyon-stone-4/",
                AfxExplorerLink.Url(stone, isStone: true));
        }
    }
}
