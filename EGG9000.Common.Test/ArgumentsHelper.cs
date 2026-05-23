using EGG9000.Bot;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace EGG9000.Common.Test {
    [TestClass]
    public class ArgumentsHelperTest {
        //[TestMethod]
        //public void TestLoop1() {
        //    for(double i = 500; i < 100000000; i++) {
        //        Assert.AreEqual(ArgumentsHelper.NumberToStringOld(i), ArgumentsHelper.NumberToString(i));
        //    }
        //}
        //[TestMethod]
        //public void TestLoopShowDecimal() {
        //    for(double i = 500; i < 100000000; i++) {
        //        Assert.AreEqual(ArgumentsHelper.NumberToStringOld(i, true), ArgumentsHelper.NumberToString(i, true));
        //    }
        //}
        //[TestMethod]
        //public void TestLoopDecimalPlaces() {
        //    for(var d = 0; d <= 3; d++) {
        //        for(double i = 500; i < 100000000; i++) {
        //            Assert.AreEqual(ArgumentsHelper.NumberToStringOld(i, false, d), ArgumentsHelper.NumberToString(i, false, d));
        //        }
        //    }
        //}

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