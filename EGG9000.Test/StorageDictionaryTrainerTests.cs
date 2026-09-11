using EGG9000.Bot.Automated;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageDictionaryTrainerTests {
        private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        [TestMethod]
        public void ShouldTrain_FingerprintMismatch_True() {
            Assert.IsTrue(StorageDictionaryTrainer.ShouldTrain("aaa", "bbb", Now, Now));
        }

        [TestMethod]
        public void ShouldTrain_NoActiveRow_True() {
            Assert.IsTrue(StorageDictionaryTrainer.ShouldTrain(null, "bbb", null, Now));
        }

        [TestMethod]
        public void ShouldTrain_SameFingerprint_Under30Days_False() {
            Assert.IsFalse(StorageDictionaryTrainer.ShouldTrain("aaa", "aaa", Now.AddDays(-29.9), Now));
        }

        [TestMethod]
        public void ShouldTrain_SameFingerprint_Over30Days_True() {
            Assert.IsTrue(StorageDictionaryTrainer.ShouldTrain("aaa", "aaa", Now.AddDays(-30), Now));
            Assert.IsTrue(StorageDictionaryTrainer.ShouldTrain("aaa", "aaa", Now.AddDays(-45), Now));
        }

        [TestMethod]
        public void ShouldAdopt_ExactlyFivePercentSmaller_True() {
            Assert.IsTrue(StorageDictionaryTrainer.ShouldAdopt(100_000, 95_000));
        }

        [TestMethod]
        public void ShouldAdopt_FourPercentSmaller_False() {
            Assert.IsFalse(StorageDictionaryTrainer.ShouldAdopt(100_000, 96_000));
        }

        [TestMethod]
        public void ShouldAdopt_Larger_False() {
            Assert.IsFalse(StorageDictionaryTrainer.ShouldAdopt(100_000, 120_000));
        }

        [TestMethod]
        public void ShouldAdopt_NoActiveBytes_False() {
            Assert.IsFalse(StorageDictionaryTrainer.ShouldAdopt(0, 0));
        }

        [TestMethod]
        public void NextId_AboveMaxUsed() {
            Assert.AreEqual(3, StorageDictionaryTrainer.NextId([1, 2]));
            Assert.AreEqual(8, StorageDictionaryTrainer.NextId([1, 2, 7]));
            Assert.AreEqual(1, StorageDictionaryTrainer.NextId([]));
        }
    }
}
