using Discord;

using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class OverflowSyncingTests {
        private const ulong MainGuild = 100;
        private const ulong OverflowGuild = 200;
        private const ulong MainRole = 11;
        private const ulong OverflowRole = 22;
        private const ulong UnmappedRole = 33;
        private const ulong SomeUser = 555;
        private const ulong SomeChannel = 777;

        private static List<RoleMap> Maps() => [new RoleMap { RoleID = MainRole, Values = [(OverflowGuild, OverflowRole)] }];

        private static CommandPermissionRest Perm(ulong id, int type, bool allow = true) => new() { Id = id, Type = type, Permission = allow };

        [TestMethod]
        public void MapPermissions_EveryoneMapsToOverflowEveryone() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainGuild, 1, false)], MainGuild, OverflowGuild, Maps());

            Assert.AreEqual(1, mapped.Count);
            Assert.AreEqual(OverflowGuild, mapped[0].Id);
            Assert.AreEqual(1, mapped[0].Type);
            Assert.IsFalse(mapped[0].Permission);
        }

        [TestMethod]
        public void MapPermissions_MappedRoleIsRewrittenToOverflowRole() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainRole, 1)], MainGuild, OverflowGuild, Maps());

            Assert.AreEqual(1, mapped.Count);
            Assert.AreEqual(OverflowRole, mapped[0].Id);
            Assert.IsTrue(mapped[0].Permission);
        }

        [TestMethod]
        public void MapPermissions_RoleWithoutOverflowCounterpartIsDropped() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(UnmappedRole, 1)], MainGuild, OverflowGuild, Maps());
            Assert.AreEqual(0, mapped.Count);
        }

        [TestMethod]
        public void MapPermissions_RoleMappedToOtherOverflowIsDropped() {
            var maps = new List<RoleMap> { new() { RoleID = MainRole, Values = [(999, OverflowRole)] } };
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainRole, 1)], MainGuild, OverflowGuild, maps);
            Assert.AreEqual(0, mapped.Count);
        }

        [TestMethod]
        public void MapPermissions_UserPassesThroughChannelIsDropped() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(SomeUser, 2), Perm(SomeChannel, 3)], MainGuild, OverflowGuild, Maps());

            Assert.AreEqual(1, mapped.Count);
            Assert.AreEqual(SomeUser, mapped[0].Id);
            Assert.AreEqual(2, mapped[0].Type);
        }

        [TestMethod]
        public void PermissionsMatch_BothNullMatchesOneNullDoesNot() {
            Assert.IsTrue(OverflowSyncing.PermissionsMatch(null, null));
            Assert.IsFalse(OverflowSyncing.PermissionsMatch([], null));
            Assert.IsFalse(OverflowSyncing.PermissionsMatch(null, []));
        }

        [TestMethod]
        public void PermissionsMatch_IsOrderInsensitive() {
            var a = new[] { Perm(MainRole, 1), Perm(SomeUser, 2, false) };
            var b = new[] { Perm(SomeUser, 2, false), Perm(MainRole, 1) };
            Assert.IsTrue(OverflowSyncing.PermissionsMatch(a, b));
        }

        [TestMethod]
        public void PermissionsMatch_DetectsFlippedPermissionAndCountMismatch() {
            Assert.IsFalse(OverflowSyncing.PermissionsMatch([Perm(MainRole, 1, true)], [Perm(MainRole, 1, false)]));
            Assert.IsFalse(OverflowSyncing.PermissionsMatch([Perm(MainRole, 1)], [Perm(MainRole, 1), Perm(SomeUser, 2)]));
        }

        [TestMethod]
        public void OverwritesMatch_ComparesAllowAndDenyBits() {
            var allowView = new OverwritePermissions(viewChannel: PermValue.Allow);
            var denyView = new OverwritePermissions(viewChannel: PermValue.Deny);
            var allowViewNoSend = new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny);

            Assert.IsTrue(OverflowSyncing.OverwritesMatch(allowView, new OverwritePermissions(viewChannel: PermValue.Allow)));
            Assert.IsFalse(OverflowSyncing.OverwritesMatch(allowView, denyView));
            Assert.IsFalse(OverflowSyncing.OverwritesMatch(allowView, allowViewNoSend));
        }

        [TestMethod]
        public void ParseRoleIds_TrimsSkipsEmptyAndHandlesNull() {
            CollectionAssert.AreEqual(new[] { "11", "22" }, OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = " 11, 22,, " }));
            Assert.AreEqual(0, OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = null }).Length);
            Assert.AreEqual(0, OverflowSyncing.ParseRoleIds(null).Length);
        }

        [TestMethod]
        public void ParseRoleIds_ExactMatchOnlyNoSubstring() {
            var ids = OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = "112" });
            Assert.IsFalse(ids.Contains("12"));
            Assert.IsTrue(ids.Contains("112"));
        }
    }
}
