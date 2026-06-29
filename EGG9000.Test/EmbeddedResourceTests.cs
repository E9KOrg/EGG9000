using System;
using System.Collections.Generic;
using System.Threading;

using EGG9000.Common.JsonData;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    public class EmbeddedResourceTests {
        private sealed class EmojiRow { }

        [TestMethod]
        public void Json_loads_and_caches_same_reference() {
            var res = EmbeddedResource.Json<List<EmojiRow>>("ArtifactEmoji.json");
            var a = res.Value;
            var b = res.Value;
            Assert.IsNotNull(a);
            Assert.IsTrue(a.Count > 0, "expected non-empty list");
            Assert.AreSame(a, b, "value should be cached");
        }

        [TestMethod]
        public void PostProcess_runs_exactly_once() {
            var runs = 0;
            var res = EmbeddedResource.Json<List<EmojiRow>>("ArtifactEmoji.json", v => { Interlocked.Increment(ref runs); return v; });
            _ = res.Value;
            _ = res.Value;
            Assert.AreEqual(1, runs, "post-process should run once");
        }

        [TestMethod]
        public void All_data_classes_load() {
            Assert.IsTrue(EGG9000.Common.JsonData.ArtifactEmoji.Get().Count > 0);
            Assert.IsTrue(EGG9000.Common.JsonData.EIEpicResearch.EiEpicResearch.Get().epicResearchItems.Count > 0);
            Assert.IsTrue(EGG9000.Common.JsonData.EiStatics.Root.Get().eggIncEggs.Count > 0);
            Assert.IsTrue(EGG9000.Common.JsonData.EiResearch.Get().Count > 0);
            Assert.IsTrue(EGG9000.Common.JsonData.EiAfxData.EiAfxDataRoot.Get().artifact_families.Count > 0);

            var afx = EGG9000.Common.JsonData.EiAfxConfig.Root.Get();
            Assert.IsTrue(afx.craftingLevelXpThresholds.Count > 0, "post-process populated XP thresholds");
            Assert.IsTrue(afx.baseCraftingCoefficients.Count > 0, "post-process populated coefficients");
        }

        [TestMethod]
        public void Missing_resource_throws_clear_message() {
            var res = EmbeddedResource.Json<List<EmojiRow>>("does-not-exist-xyz.json");
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = res.Value);
            Assert.IsTrue(ex.Message.Contains("does-not-exist-xyz.json"), "message should name the missing suffix");
        }
    }
}
