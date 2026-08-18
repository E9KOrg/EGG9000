using EGG9000.Onboarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class OnboardOptionsTests {
        [TestMethod]
        public void Parse_NoArgs_LeavesEverythingUnset() {
            var options = OnboardOptions.Parse([]);
            Assert.IsNull(options.GuildId);
            Assert.IsNull(options.AdminDiscordId);
            Assert.IsFalse(options.NoWait);
        }

        // Running this project is itself the setup command, so "--onboard" no longer means
        // anything. Developers who learned the old invocation must not get an error for it.
        [TestMethod]
        public void Parse_LegacyOnboardFlag_IsIgnored() {
            var options = OnboardOptions.Parse(["--onboard"]);
            Assert.IsNull(options.GuildId);
            Assert.IsNull(options.AdminDiscordId);
            Assert.IsFalse(options.NoWait);
        }

        [TestMethod]
        public void Parse_GuildAndAdmin_AreRead() {
            var options = OnboardOptions.Parse(["--guild", "1108127105088241746", "--admin", "123456789"]);
            Assert.AreEqual(1108127105088241746UL, options.GuildId);
            Assert.AreEqual(123456789UL, options.AdminDiscordId);
        }

        [TestMethod]
        public void Parse_NoWaitFlag_IsRead() {
            var options = OnboardOptions.Parse(["--no-wait"]);
            Assert.IsTrue(options.NoWait);
        }

        [TestMethod]
        public void Parse_NonNumericGuild_Throws() {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => OnboardOptions.Parse(["--guild", "not-a-number"]));
            StringAssert.Contains(ex.Message, "--guild");
        }

        [TestMethod]
        public void Parse_GuildWithoutValue_Throws() {
            Assert.ThrowsExactly<ArgumentException>(() => OnboardOptions.Parse(["--guild"]));
        }

        // A dropped flag is worse than a rejected one. Silently ignoring a mistyped --admin lets a
        // scripted run skip the grant, exit 0 and report success with nobody made Admin.
        [TestMethod]
        public void Parse_UnrecognisedFlag_Throws() {
            var ex = Assert.ThrowsExactly<ArgumentException>(() => OnboardOptions.Parse(["--adminid", "123"]));
            StringAssert.Contains(ex.Message, "--adminid");
        }

        [TestMethod]
        public void Parse_SingleDashTypo_Throws() {
            Assert.ThrowsExactly<ArgumentException>(() => OnboardOptions.Parse(["-admin", "123"]));
        }

        [TestMethod]
        public void OnboardResult_FactoryMethods_SetOutcome() {
            Assert.AreEqual(OnboardOutcome.Created, OnboardResult.Created("x").Outcome);
            Assert.AreEqual(OnboardOutcome.AlreadyExisted, OnboardResult.AlreadyExisted("x").Outcome);
            Assert.AreEqual(OnboardOutcome.Skipped, OnboardResult.Skipped("x").Outcome);
            Assert.AreEqual(OnboardOutcome.Failed, OnboardResult.Failed("x").Outcome);
        }

        [TestMethod]
        public void OnboardRoles_AdminMatchesTheIdentityRoleNameUsedAcrossTheSite() {
            // Tasks 8 and 9 are written in parallel and both consume this constant, so it must
            // exist here rather than on either step. The value must match the role name the site
            // authorizes against, for example [Authorize(Roles = "Admin")].
            Assert.AreEqual("Admin", OnboardRoles.Admin);
        }
    }
}
