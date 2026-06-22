using EGG9000.Bot.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Test {
    [TestClass]
    public class RoleToggleTests {
        [TestMethod]
        public void Add_when_missing_and_should_and_canAdd() {
            Assert.AreEqual(RoleToggle.RoleAction.Add, RoleToggle.Decide(hasRole: false, shouldHave: true, canAdd: true));
        }

        [TestMethod]
        public void No_add_when_canAdd_false() {
            Assert.AreEqual(RoleToggle.RoleAction.None, RoleToggle.Decide(hasRole: false, shouldHave: true, canAdd: false));
        }

        [TestMethod]
        public void Remove_when_has_and_should_not() {
            Assert.AreEqual(RoleToggle.RoleAction.Remove, RoleToggle.Decide(hasRole: true, shouldHave: false, canAdd: true));
        }

        [TestMethod]
        public void Remove_ignores_canAdd() {
            Assert.AreEqual(RoleToggle.RoleAction.Remove, RoleToggle.Decide(hasRole: true, shouldHave: false, canAdd: false));
        }

        [TestMethod]
        public void None_when_has_and_should() {
            Assert.AreEqual(RoleToggle.RoleAction.None, RoleToggle.Decide(hasRole: true, shouldHave: true));
        }

        [TestMethod]
        public void None_when_missing_and_should_not() {
            Assert.AreEqual(RoleToggle.RoleAction.None, RoleToggle.Decide(hasRole: false, shouldHave: false));
        }
    }
}
