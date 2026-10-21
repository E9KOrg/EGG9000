using Discord;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers.Discord;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class OverflowSyncingDiscordTests {
        private const ulong Main = 100;
        private const ulong Overflow = 200;
        private const ulong Overflow2 = 300;
        private const ulong MainMod = 11;
        private const ulong MainVip = 12;
        private const ulong MainGhost = 13;
        private const ulong OverflowMod = 21;
        private const ulong OverflowVip = 22;
        private const ulong OverflowExtra = 23;
        private const ulong Overflow2Mod = 31;
        private const ulong Overflow2Ghost = 33;
        private const ulong SomeUser = 5;
        private const ulong OtherUser = 6;

        private static readonly OverwritePermissions AllowView = new(viewChannel: PermValue.Allow);
        private static readonly OverwritePermissions DenyView = new(viewChannel: PermValue.Deny);

        private static Guild DbGuild(string? rolesToSync = null, string coopCategories = "", params ulong[] overflows) => new() {
            Id = Main,
            DiscordSeverId = Main,
            Name = "main",
            RolesToSync = rolesToSync,
            CoopCategories = coopCategories,
            OverflowServersJson = $"[{string.Join(",", overflows)}]"
        };

        private static List<string> Endpoints(FakeDiscord discord) => [.. discord.Writes.Select(x => string.Join(' ', x.Split(' ').Take(2)))];

        [TestMethod]
        public void GetRolesToSync_ReturnsOnlyListedRolesThatExistInMain() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");
            discord.Role(main, MainVip, "Vip");

            var roles = OverflowSyncing.GetRolesToSync(DbGuild($"{MainMod}, 999, junk,,{MainMod}"), main);

            CollectionAssert.AreEqual(new[] { MainMod }, roles.Select(x => x.Id).ToArray());
        }

        [TestMethod]
        [DataRow((string?)null)]
        [DataRow("")]
        [DataRow(" , ")]
        [DataRow("not-a-role")]
        public void GetRolesToSync_BlankOrGarbageSelectsNothing(string? raw) {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");

            Assert.AreEqual(0, OverflowSyncing.GetRolesToSync(DbGuild(raw), main).Count);
        }

        [TestMethod]
        public void GetRolesToSync_EveryoneIsSelectableByItsGuildId() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);

            var roles = OverflowSyncing.GetRolesToSync(DbGuild(Main.ToString()), main);

            Assert.AreEqual(1, roles.Count);
            Assert.IsTrue(roles[0].IsEveryone);
        }

        [TestMethod]
        public void GetRoleMaps_MapsByNamePerOverflowAndSkipsRolesAnOverflowLacks() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var mod = discord.Role(main, MainMod, "Mod");
            var ghost = discord.Role(main, MainGhost, "Ghost");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            var overflow2 = discord.Guild(Overflow2);
            discord.Role(overflow2, Overflow2Mod, "Mod");
            discord.Role(overflow2, Overflow2Ghost, "Ghost");

            var maps = OverflowSyncing.GetRoleMaps([mod, ghost], [overflow, overflow2]);

            Assert.AreEqual(2, maps.Count);
            CollectionAssert.AreEqual(new[] { (Overflow, OverflowMod), (Overflow2, Overflow2Mod) }, maps.Single(x => x.RoleID == MainMod).Values);
            CollectionAssert.AreEqual(new[] { (Overflow2, Overflow2Ghost) }, maps.Single(x => x.RoleID == MainGhost).Values);
        }

        [TestMethod]
        public void GetRoleMaps_NameMatchIsCaseSensitive() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var mod = discord.Role(main, MainMod, "Mod");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "mod");

            var maps = OverflowSyncing.GetRoleMaps([mod], [overflow]);

            Assert.AreEqual(0, maps.Single().Values.Count);
        }

        [TestMethod]
        public void GetRoleMaps_DuplicateOverflowNamesYieldOneMapping() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var mod = discord.Role(main, MainMod, "Mod");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            discord.Role(overflow, OverflowExtra, "Mod");

            var values = OverflowSyncing.GetRoleMaps([mod], [overflow]).Single().Values;

            Assert.AreEqual(1, values.Count);
            CollectionAssert.Contains(new[] { OverflowMod, OverflowExtra }, values[0].RoleId);
        }

        [TestMethod]
        public void RoleMapsFeedPermissionMapping_RoleMissingFromOneOverflowIsDroppedOnlyThere() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var mod = discord.Role(main, MainMod, "Mod");
            var ghost = discord.Role(main, MainGhost, "Ghost");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            var overflow2 = discord.Guild(Overflow2);
            discord.Role(overflow2, Overflow2Mod, "Mod");
            discord.Role(overflow2, Overflow2Ghost, "Ghost");
            var maps = OverflowSyncing.GetRoleMaps([mod, ghost], [overflow, overflow2]);
            CommandPermissionRest[] source = [
                new() { Id = MainMod, Type = 1, Permission = true },
                new() { Id = MainGhost, Type = 1, Permission = false }
            ];

            var first = OverflowSyncing.MapPermissionsToOverflow(source, Main, Overflow, maps);
            var second = OverflowSyncing.MapPermissionsToOverflow(source, Main, Overflow2, maps);

            CollectionAssert.AreEqual(new[] { OverflowMod }, first.Select(x => x.Id).ToArray());
            CollectionAssert.AreEqual(new[] { Overflow2Mod, Overflow2Ghost }, second.Select(x => x.Id).ToArray());
            Assert.IsFalse(second.Single(x => x.Id == Overflow2Ghost).Permission);
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_NullGuildIsMissing() {
            var discord = new FakeDiscord();
            Assert.IsTrue(await OverflowSyncing.IsMissingFromAnyOverflowAsync(null!, discord.Hosted(), SomeUser));
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_NoOverflowsIsNeverMissing() {
            var discord = new FakeDiscord();
            Assert.IsFalse(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(), discord.Hosted(), SomeUser));
            Assert.AreEqual(0, discord.Calls.Count);
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_OverflowTheBotCannotSeeCountsAsMissing() {
            var discord = new FakeDiscord();
            var overflow = discord.Guild(Overflow);
            discord.User(overflow, SomeUser);

            Assert.IsTrue(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(null, "", Overflow, Overflow2), discord.Hosted(), SomeUser));
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_CachedInEveryOverflowSkipsRest() {
            var discord = new FakeDiscord();
            discord.User(discord.Guild(Overflow), SomeUser);
            discord.User(discord.Guild(Overflow2), SomeUser);

            Assert.IsFalse(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(null, "", Overflow, Overflow2), discord.Hosted(), SomeUser));
            Assert.AreEqual(0, discord.Calls.Count);
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_CacheMissButRestPresentIsNotMissing() {
            var discord = new FakeDiscord();
            discord.Guild(Overflow);
            discord.RestMember(Overflow, SomeUser);

            Assert.IsFalse(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(null, "", Overflow), discord.Hosted(), SomeUser));
            CollectionAssert.Contains(discord.Calls, $"GET guilds/{Overflow}/members/{SomeUser}");
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_RestNotFoundIsMissing() {
            var discord = new FakeDiscord();
            discord.Guild(Overflow);

            Assert.IsTrue(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(null, "", Overflow), discord.Hosted(), SomeUser));
        }

        [TestMethod]
        public async Task IsMissingFromAnyOverflow_RestFailureIsTreatedAsMissing() {
            var discord = new FakeDiscord();
            discord.Guild(Overflow);
            discord.RestMember(Overflow, SomeUser);
            discord.FailMemberLookups(Overflow);

            Assert.IsTrue(await OverflowSyncing.IsMissingFromAnyOverflowAsync(DbGuild(null, "", Overflow), discord.Hosted(), SomeUser));
        }

        [TestMethod]
        public async Task HandleRoleSyncs_NothingConfiguredWritesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");
            var overflow = discord.Guild(Overflow);
            discord.User(main, SomeUser, [MainMod]);
            discord.User(overflow, SomeUser);

            await OverflowSyncing.HandleRoleSyncsAsync(DbGuild(null), main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            Assert.AreEqual(0, discord.Calls.Count);
        }

        [TestMethod]
        public async Task HandleRoleSyncs_CreatesMissingRolesWithPermissionsAndUpdatesOnPermissionOrColorDrift() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod", new GuildPermissions(kickMembers: true), 0xFF0000);
            discord.Role(main, MainVip, "Vip", default, 0x00FF00);
            discord.Role(main, MainGhost, "Ghost", new GuildPermissions(kickMembers: true));
            discord.Role(main, 14, "Banned", new GuildPermissions(banMembers: true));
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod", new GuildPermissions(kickMembers: true), 0xFF0000);
            discord.Role(overflow, OverflowVip, "Vip", default, 0x0000FF);
            discord.Role(overflow, 24, "Banned", default);

            await OverflowSyncing.HandleRoleSyncsAsync(DbGuild($"{MainMod},{MainVip},{MainGhost},14"), main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] {
                    $"POST guilds/{Overflow}/roles",
                    $"PATCH guilds/{Overflow}/roles/{OverflowVip}",
                    $"PATCH guilds/{Overflow}/roles/24"
                },
                Endpoints(discord));
            var created = discord.Writes.Single(x => x.StartsWith("POST "));
            StringAssert.Contains(created, "\"name\":\"Ghost\"");
            StringAssert.Contains(created, "\"permissions\":\"2\"");
            StringAssert.Contains(discord.Writes.Single(x => x.Contains("/roles/24")), "\"permissions\":\"4\"");
        }

        [TestMethod]
        public async Task HandleRoleSyncs_MembershipFollowsMainByRoleNameAndLeavesUnsyncedRolesAlone() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");
            discord.Role(main, MainVip, "Vip");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            discord.Role(overflow, OverflowVip, "Vip");
            discord.Role(overflow, 29, "Local");
            discord.User(main, SomeUser, [MainMod]);
            discord.User(overflow, SomeUser, [OverflowVip, 29]);
            discord.User(overflow, OtherUser, [OverflowVip]);

            await OverflowSyncing.HandleRoleSyncsAsync(DbGuild($"{MainMod},{MainVip}"), main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] {
                    $"PUT guilds/{Overflow}/members/{SomeUser}/roles/{OverflowMod}",
                    $"DELETE guilds/{Overflow}/members/{SomeUser}/roles/{OverflowVip}"
                },
                Endpoints(discord));
        }

        [TestMethod]
        public async Task HandleRoleSyncs_RoleCreatedThisPassIsNotAssignedUntilTheNextPass() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainGhost, "Ghost");
            var overflow = discord.Guild(Overflow);
            discord.User(main, SomeUser, [MainGhost]);
            discord.User(overflow, SomeUser);

            await OverflowSyncing.HandleRoleSyncsAsync(DbGuild(MainGhost.ToString()), main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            CollectionAssert.AreEqual(new[] { $"POST guilds/{Overflow}/roles" }, Endpoints(discord));
        }

        [TestMethod]
        public async Task HandleRoleSyncs_CancelledTokenWritesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainGhost, "Ghost");
            var overflow = discord.Guild(Overflow);

            await OverflowSyncing.HandleRoleSyncsAsync(DbGuild(MainGhost.ToString()), main, [overflow], NullLogger.Instance, new CancellationToken(true));

            Assert.AreEqual(0, discord.Calls.Count);
        }

        [TestMethod]
        public async Task HandleChannelPermissionSyncs_MirrorsMainRoleOverwritesOntoEveryOverflowCoopCategory() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");
            discord.Category(main, 700, "Coops",
                new Overwrite(MainMod, PermissionTarget.Role, AllowView),
                new Overwrite(Main, PermissionTarget.Role, DenyView),
                new Overwrite(19, PermissionTarget.Role, AllowView),
                new Overwrite(SomeUser, PermissionTarget.User, AllowView));
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            discord.Role(overflow, OverflowExtra, "Extra");
            discord.User(overflow, OtherUser);
            discord.Category(overflow, 800, "Coops A",
                new Overwrite(Overflow, PermissionTarget.Role, DenyView),
                new Overwrite(OverflowMod, PermissionTarget.Role, DenyView),
                new Overwrite(OverflowExtra, PermissionTarget.Role, AllowView),
                new Overwrite(OtherUser, PermissionTarget.User, AllowView));
            discord.Category(overflow, 801, "co-ops b");
            discord.Category(overflow, 802, "Coops Finished", new Overwrite(OverflowExtra, PermissionTarget.Role, AllowView));
            discord.Category(overflow, 803, "General", new Overwrite(OverflowExtra, PermissionTarget.Role, AllowView));
            var hosted = discord.Hosted(DbGuild(null, "700", Overflow));

            await OverflowSyncing.HandleChannelPermissionSyncsAsync(hosted, main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] {
                    $"PUT channels/800/permissions/{OverflowMod}",
                    $"DELETE channels/800/permissions/{OverflowExtra}",
                    $"DELETE channels/800/permissions/{OtherUser}",
                    $"PUT channels/801/permissions/{OverflowMod}",
                    $"PUT channels/801/permissions/{Overflow}"
                },
                Endpoints(discord));
            StringAssert.Contains(discord.Writes.Single(x => x.StartsWith($"PUT channels/800/permissions/{OverflowMod}")), "\"allow\":\"1024\"");
            StringAssert.Contains(discord.Writes.Single(x => x.StartsWith($"PUT channels/801/permissions/{Overflow}")), "\"deny\":\"1024\"");
        }

        [TestMethod]
        public async Task HandleChannelPermissionSyncs_MatchingOverwritesWriteNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, MainMod, "Mod");
            discord.Category(main, 700, "Coops", new Overwrite(MainMod, PermissionTarget.Role, AllowView));
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            discord.Category(overflow, 800, "Coops", new Overwrite(OverflowMod, PermissionTarget.Role, AllowView));
            var hosted = discord.Hosted(DbGuild(null, "700", Overflow));

            await OverflowSyncing.HandleChannelPermissionSyncsAsync(hosted, main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            Assert.AreEqual(0, discord.Writes.Count());
        }

        [TestMethod]
        public async Task HandleChannelPermissionSyncs_NoMainCoopCategorySkipsWithoutThrowing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowExtra, "Extra");
            discord.Category(overflow, 800, "Coops", new Overwrite(OverflowExtra, PermissionTarget.Role, AllowView));
            var hosted = discord.Hosted(DbGuild(null, "", Overflow));

            await OverflowSyncing.HandleChannelPermissionSyncsAsync(hosted, main, [overflow], NullLogger.Instance, TestContext!.CancellationToken);

            Assert.AreEqual(0, discord.Writes.Count());
        }

        [TestMethod]
        public async Task SyncRoleUpdate_PushesRenameAndPermissionsToTheSameNamedOverflowRole() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var original = discord.Role(main, MainMod, "Mod");
            var updated = discord.Role(main, MainMod, "Moderator", new GuildPermissions(kickMembers: true));
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            discord.Role(overflow, OverflowExtra, "Moderator");
            await using var db = await Db(DbGuild(MainMod.ToString(), "", Overflow));

            await OverflowSyncing.SyncRoleUpdateAsync(original, updated, db, discord.Hosted(), NullLogger.Instance);

            var write = discord.Writes.Single();
            StringAssert.StartsWith(write, $"PATCH guilds/{Overflow}/roles/{OverflowMod} ");
            StringAssert.Contains(write, "\"name\":\"Moderator\"");
            StringAssert.Contains(write, "\"permissions\":\"2\"");
        }

        [TestMethod]
        public async Task SyncRoleUpdate_IgnoresRolesNotListedExactly() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var original = discord.Role(main, MainMod, "Mod");
            var overflow = discord.Guild(Overflow);
            discord.Role(overflow, OverflowMod, "Mod");
            await using var db = await Db(DbGuild("111,211", "", Overflow));

            await OverflowSyncing.SyncRoleUpdateAsync(original, original, db, discord.Hosted(), NullLogger.Instance);

            Assert.AreEqual(0, discord.Calls.Count);
        }

        [TestMethod]
        public async Task SyncRoleUpdate_GuildWithoutOverflowsOrUnknownGuildDoesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var original = discord.Role(main, MainMod, "Mod");
            var stranger = discord.Guild(999);
            var strangerRole = discord.Role(stranger, 998, "Mod");
            discord.Role(discord.Guild(Overflow), OverflowMod, "Mod");
            await using var db = await Db(DbGuild(MainMod.ToString(), ""));

            await OverflowSyncing.SyncRoleUpdateAsync(original, original, db, discord.Hosted(), NullLogger.Instance);
            await OverflowSyncing.SyncRoleUpdateAsync(strangerRole, strangerRole, db, discord.Hosted(), NullLogger.Instance);
            await OverflowSyncing.SyncRoleUpdateAsync(null!, null!, db, discord.Hosted(), NullLogger.Instance);

            Assert.AreEqual(0, discord.Calls.Count);
        }

        private static async Task<ApplicationDbContext> Db(Guild guild) {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Guilds.Add(guild);
            await db.SaveChangesAsync();
            return db;
        }

        public TestContext? TestContext { get; set; }
    }
}
