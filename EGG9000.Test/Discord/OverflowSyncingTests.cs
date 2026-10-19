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

        [TestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(",")]
        [DataRow(" , ,, ")]
        public void ParseRoleIds_BlankOrSeparatorOnlyYieldsNothing(string raw) {
            Assert.AreEqual(0, OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = raw }).Length);
        }

        [TestMethod]
        public void ParseRoleIds_GarbageIsKeptVerbatimAndNeverMatchesARoleId() {
            var ids = OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = "abc, 11 ,-5,11x" });

            CollectionAssert.AreEqual(new[] { "abc", "11", "-5", "11x" }, ids);
            Assert.AreEqual(1, ids.Count(x => x == MainRole.ToString()));
        }

        [TestMethod]
        public void ParseRoleIds_SingleIdWithoutSeparator() {
            CollectionAssert.AreEqual(new[] { "11" }, OverflowSyncing.ParseRoleIds(new Guild { RolesToSync = "11" }));
        }

        [TestMethod]
        public void MapPermissions_EmptyInputYieldsEmptyList() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([], MainGuild, OverflowGuild, Maps());
            Assert.IsNotNull(mapped);
            Assert.AreEqual(0, mapped.Count);
        }

        [TestMethod]
        public void MapPermissions_EmptyRoleMapsStillMapsEveryoneAndUsers() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainGuild, 1), Perm(MainRole, 1), Perm(SomeUser, 2, false)], MainGuild, OverflowGuild, []);

            Assert.AreEqual(2, mapped.Count);
            Assert.IsTrue(mapped.Any(x => x.Id == OverflowGuild && x.Type == 1 && x.Permission));
            Assert.IsTrue(mapped.Any(x => x.Id == SomeUser && x.Type == 2 && !x.Permission));
        }

        [TestMethod]
        public void MapPermissions_ZeroOverflowRoleIdIsDropped() {
            var maps = new List<RoleMap> { new() { RoleID = MainRole, Values = [(OverflowGuild, 0)] } };
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainRole, 1)], MainGuild, OverflowGuild, maps);
            Assert.AreEqual(0, mapped.Count);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void MapPermissions_PreservesAllowAndDenyForEveryKind(bool allow) {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(MainGuild, 1, allow), Perm(MainRole, 1, allow), Perm(SomeUser, 2, allow)], MainGuild, OverflowGuild, Maps());

            Assert.AreEqual(3, mapped.Count);
            Assert.IsTrue(mapped.All(x => x.Permission == allow));
        }

        [TestMethod]
        public void MapPermissions_PicksTheMappingForTheTargetOverflow() {
            const ulong otherOverflow = 300;
            const ulong otherOverflowRole = 44;
            var maps = new List<RoleMap> { new() { RoleID = MainRole, Values = [(otherOverflow, otherOverflowRole), (OverflowGuild, OverflowRole)] } };

            var first = OverflowSyncing.MapPermissionsToOverflow([Perm(MainRole, 1)], MainGuild, OverflowGuild, maps);
            var second = OverflowSyncing.MapPermissionsToOverflow([Perm(MainRole, 1)], MainGuild, otherOverflow, maps);

            Assert.AreEqual(OverflowRole, first.Single().Id);
            Assert.AreEqual(otherOverflowRole, second.Single().Id);
        }

        [TestMethod]
        public void MapPermissions_UnknownTypeIsDropped() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(SomeUser, 9)], MainGuild, OverflowGuild, Maps());
            Assert.AreEqual(0, mapped.Count);
        }

        [TestMethod]
        public void MapPermissions_MixedSetKeepsOrderOfSurvivors() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow(
                [Perm(SomeChannel, 3), Perm(MainRole, 1), Perm(UnmappedRole, 1), Perm(MainGuild, 1, false), Perm(SomeUser, 2)],
                MainGuild,
                OverflowGuild,
                Maps());

            CollectionAssert.AreEqual(new[] { OverflowRole, OverflowGuild, SomeUser }, mapped.Select(x => x.Id).ToArray());
        }

        [TestMethod]
        public void MapPermissions_MappedResultMatchesItselfSoASecondSyncIsANoOp() {
            var source = new[] { Perm(MainGuild, 1, false), Perm(MainRole, 1), Perm(SomeUser, 2) };
            var first = OverflowSyncing.MapPermissionsToOverflow(source, MainGuild, OverflowGuild, Maps());
            var second = OverflowSyncing.MapPermissionsToOverflow(source, MainGuild, OverflowGuild, Maps());

            Assert.IsTrue(OverflowSyncing.PermissionsMatch(first, second));
        }

        [TestMethod]
        public void PermissionsMatch_EmptyMatchesEmpty() {
            Assert.IsTrue(OverflowSyncing.PermissionsMatch([], []));
        }

        [TestMethod]
        public void PermissionsMatch_DistinguishesTypeWithSameId() {
            Assert.IsFalse(OverflowSyncing.PermissionsMatch([Perm(SomeUser, 1)], [Perm(SomeUser, 2)]));
        }

        [TestMethod]
        public void PermissionsMatch_DistinguishesId() {
            Assert.IsFalse(OverflowSyncing.PermissionsMatch([Perm(MainRole, 1)], [Perm(OverflowRole, 1)]));
        }

        [TestMethod]
        public void PermissionsMatch_MappedEmptyAgainstMissingOverflowEntryIsAChange() {
            var mapped = OverflowSyncing.MapPermissionsToOverflow([Perm(SomeChannel, 3)], MainGuild, OverflowGuild, Maps());
            Assert.IsFalse(OverflowSyncing.PermissionsMatch(mapped, null));
        }

        [TestMethod]
        public void OverwritesMatch_InheritEverywhereMatchesInherit() {
            Assert.IsTrue(OverflowSyncing.OverwritesMatch(OverwritePermissions.InheritAll, new OverwritePermissions()));
        }

        [TestMethod]
        public void OverwritesMatch_InheritDiffersFromExplicitDeny() {
            Assert.IsFalse(OverflowSyncing.OverwritesMatch(OverwritePermissions.InheritAll, new OverwritePermissions(sendMessages: PermValue.Deny)));
        }

        [TestMethod]
        public void OverwritesMatch_IsSymmetric() {
            var a = new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Deny);
            var b = new OverwritePermissions(viewChannel: PermValue.Allow);

            Assert.AreEqual(OverflowSyncing.OverwritesMatch(a, b), OverflowSyncing.OverwritesMatch(b, a));
            Assert.IsTrue(OverflowSyncing.OverwritesMatch(a, new OverwritePermissions(a.AllowValue, a.DenyValue)));
        }
    }
}
