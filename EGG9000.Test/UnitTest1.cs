using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StringToTimeSpanTest {
        private DateTimeOffset startTime = new DateTimeOffset(2000,1,1,0,0,0,0,TimeSpan.Zero);
        [TestMethod]
        public void Parse1s() {
            var time = "1s".AddTimeSpanString(startTime);
            Assert.AreEqual(startTime.AddSeconds(1), time);
        }
        [TestMethod]
        public void Parse1_sec() {
            var time = "1 s".AddTimeSpanString(startTime);
            Assert.AreEqual(startTime.AddSeconds(1), time);
        }
        [TestMethod]
        public void Parse2_5w() {
            var time = "2.5w".AddTimeSpanString(startTime);
            Assert.AreEqual(startTime.AddDays(2.5 * 7), time);
        }
        [TestMethod]
        public void Parse3M() {
            var time = "3M".AddTimeSpanString(startTime);
            Assert.AreEqual(startTime.AddDays(30 * 3), time);
        }
        [TestMethod]
        public void Parse2m() {
            var time = "2m".AddTimeSpanString(startTime);
            Assert.AreEqual(startTime.AddMinutes(2), time);
        }
    }
}