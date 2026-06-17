using System.Linq;

using EGG9000.Bot.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    public class RankRegistryTests {
        [TestMethod]
        public void Registry_has_52_ranks_ordered_by_oom() {
            Assert.HasCount(52, RankRegistry.All);
            for(int i = 0; i < 52; i++) Assert.AreEqual(i, RankRegistry.All[i].Oom);
        }

        [TestMethod]
        public void ForOom_clamps() {
            Assert.AreEqual(0, RankRegistry.ForOom(-5).Oom);
            Assert.AreEqual(51, RankRegistry.ForOom(99).Oom);
        }

        [TestMethod]
        public void ForEB_maps_boundaries() {
            Assert.AreEqual(0, RankRegistry.ForEB(100).Oom);
            Assert.AreEqual(1, RankRegistry.ForEB(1000).Oom);
            Assert.AreEqual(3, RankRegistry.ForEB(100000).Oom);
            Assert.AreEqual(0, RankRegistry.ForEB(0).Oom);
        }

        [TestMethod]
        public void Infinifarmer_is_single_tier() {
            var inf = RankRegistry.ForOom(51);
            Assert.IsTrue(inf.IsInfinifarmer);
            Assert.AreEqual(1, inf.SubRank);
            Assert.AreEqual("Infinifarmer", inf.RoleName);
        }

        [TestMethod]
        public void Group_and_subrank() {
            var r = RankRegistry.ForOom(4);
            Assert.AreEqual(3, r.GroupBase);
            Assert.AreEqual(2, r.SubRank);
            Assert.AreEqual("Kilofarmer II", r.RoleName);
        }

        [TestMethod]
        public void RoleName_for_lead_has_roman_one() {
            Assert.AreEqual("Farmer I", RankRegistry.ForOom(0).RoleName);
        }

        [TestMethod]
        public void Color_parses() {
            Assert.AreEqual(new Discord.Color(0xd43500).RawValue, RankRegistry.ForOom(0).Color.RawValue);
        }

        [TestMethod]
        public void GroupLeads_count_is_18() {
            Assert.HasCount(18, RankRegistry.GroupLeads);
            Assert.IsTrue(RankRegistry.GroupLeads.All(r => r.IsGroupLead));
        }

        [TestMethod]
        public void SIPrefix_GetAllFarmerRoles_has_52_ending_infinifarmer() {
            var roles = SIPrefix.GetAllFarmerRoles();
            Assert.HasCount(52, roles);
            Assert.AreEqual("Infinifarmer", roles.Last());
            Assert.AreEqual("Farmer I", roles.First());
        }
    }
}
