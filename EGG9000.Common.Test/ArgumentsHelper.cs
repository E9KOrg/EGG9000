using EGG9000.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace EGG9000.Common.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ArgumentsHelperTest {

        [TestMethod]
        public void TestNumberFromStringDouble1() {
            var value = ArgumentsHelper.NumberFromStringDouble("1.1K");
            Assert.AreEqual(1100, value);

        }
        [TestMethod]
        public void TestNumberFromStringDouble2() {
            var value = ArgumentsHelper.NumberFromStringDouble("1Td");
            Assert.AreEqual(1 * Math.Pow(10,42), value);

        }
        [TestMethod]
        public void TestNumberFromStringDouble3() {
            var value = ArgumentsHelper.NumberFromStringDouble("1.1");
            Assert.AreEqual(1.1, value);

        }
        [TestMethod]
        public void TestNumberFromStringDouble4() {
            var value = ArgumentsHelper.NumberFromStringDouble("999D");
            Assert.AreEqual(999*Math.Pow(10,39), value);

        }
    }
}