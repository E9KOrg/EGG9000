using EGG9000.Common.Database;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AccountFormulasTests {
        [TestMethod]
        public void MerValue_ZeroOrNegativeSoulEggs_ReturnsZero() {
            Assert.AreEqual(0.0, AccountFormulas.MerValue(0, 10));
            Assert.AreEqual(0.0, AccountFormulas.MerValue(-5, 10));
        }

        [TestMethod]
        public void MerValue_NonFiniteSoulEggs_ReturnsZero() {
            Assert.AreEqual(0.0, AccountFormulas.MerValue(double.PositiveInfinity, 10));
            Assert.AreEqual(0.0, AccountFormulas.MerValue(double.NaN, 10));
        }

        [TestMethod]
        public void MerValue_KnownInput_MatchesFormula() {
            var result = AccountFormulas.MerValue(1e21, 0);
            Assert.AreEqual(47.3, result, 0.0001);
        }

        [TestMethod]
        public void TotalGeInPiggyBank_UnderTwoBreaks_UsesTwoPercentBonus() {
            Assert.AreEqual((ulong)(1000 * 1.02), AccountFormulas.TotalGeInPiggyBank(1000, 0));
            Assert.AreEqual((ulong)(1000 * 1.02), AccountFormulas.TotalGeInPiggyBank(1000, 1));
        }

        [TestMethod]
        public void TotalGeInPiggyBank_TwoBreaks_UsesTwentyFivePercentBonus() {
            Assert.AreEqual((ulong)(1000 * 1.25), AccountFormulas.TotalGeInPiggyBank(1000, 2));
        }

        [TestMethod]
        public void TotalGeInPiggyBank_ThreeOrMoreBreaks_UsesTieredFormula() {
            Assert.AreEqual(1501ul, AccountFormulas.TotalGeInPiggyBank(1000, 3));
        }

        [TestMethod]
        public void TotalGeInPiggyBank_ExtremeValues_WrapsRatherThanThrowing() {
            Assert.AreEqual(184467440737095514ul, AccountFormulas.TotalGeInPiggyBank(ulong.MaxValue, 10));
        }

        [TestMethod]
        public void PeFromTrophies_NullList_ReturnsNegativeOne() {
            Assert.AreEqual(-1, AccountFormulas.PeFromTrophies(null));
        }

        [TestMethod]
        public void PeFromTrophies_WrongCount_Throws() {
            Assert.ThrowsExactly<Exception>(() => AccountFormulas.PeFromTrophies(Enumerable.Repeat(0u, 5).ToList()));
        }

        [TestMethod]
        public void PeFromTrophies_AllBelowDiamond_ReturnsZero() {
            var levels = Enumerable.Repeat(0u, 19).ToList();
            Assert.AreEqual(0, AccountFormulas.PeFromTrophies(levels));
        }

        [TestMethod]
        public void PeFromTrophies_SomeAtDiamond_SumsWeights() {
            var levels = Enumerable.Repeat(0u, 19).ToList();
            levels[(int)Ei.Egg.Edible - 1] = (uint)TrophyLevel.Diamond;
            levels[(int)Ei.Egg.Superfood - 1] = (uint)TrophyLevel.Diamond;
            levels[(int)Ei.Egg.Medical - 1] = (uint)TrophyLevel.Diamond;
            levels[(int)Ei.Egg.RocketFuel - 1] = (uint)TrophyLevel.Diamond;

            Assert.AreEqual(5 + 4 + 3 + 2, AccountFormulas.PeFromTrophies(levels));
        }

        [TestMethod]
        public void PeFromTrophies_EnlightenmentAtDiamond_AccumulatesAllTiers() {
            var levels = Enumerable.Repeat(0u, 19).ToList();
            levels[(int)Ei.Egg.Enlightenment - 1] = (uint)TrophyLevel.Diamond;

            Assert.AreEqual(10 + 5 + 3 + 2 + 1, AccountFormulas.PeFromTrophies(levels));
        }
    }
}
