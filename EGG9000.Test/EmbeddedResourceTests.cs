using EGG9000.Common.JsonData;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Threading;

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
            Assert.IsNotEmpty(a, "expected non-empty list");
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
            Assert.IsNotEmpty(EGG9000.Common.JsonData.ArtifactEmoji.Get());
            Assert.IsNotEmpty(EGG9000.Common.JsonData.EiEpicResearch.Get().epicResearchItems);
            Assert.IsNotEmpty(EGG9000.Common.JsonData.Root.Get().eggIncEggs);
            Assert.IsNotEmpty(EGG9000.Common.JsonData.EiResearch.Get());
            Assert.IsNotEmpty(EGG9000.Common.JsonData.EiAfxDataRoot.Get().artifact_families);

            var afx = EGG9000.Common.JsonData.EiAfxConfig.Root.Get();
            Assert.IsNotEmpty(afx.craftingLevelXpThresholds, "post-process populated XP thresholds");
            Assert.IsNotEmpty(afx.baseCraftingCoefficients, "post-process populated coefficients");
        }

        [TestMethod]
        public void Missing_resource_throws_clear_message() {
            var res = EmbeddedResource.Json<List<EmojiRow>>("does-not-exist-xyz.json");
            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _ = res.Value);
            Assert.Contains("does-not-exist-xyz.json", ex.Message, "message should name the missing suffix");
        }
    }
}
