using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public class ManageOverflowTests {
        private const ulong Main = 100;
        private const ulong Overflow = 200;
        private const ulong Overflow2 = 300;
        private const ulong OtherGuild = 400;
        private const ulong MemberRole = 50;
        private const ulong OverflowOwner = 9;

        private static Guild DbGuild(ulong id = Main, params ulong[] overflows) => new() {
            Id = id,
            DiscordSeverId = id,
            Name = $"guild{id}",
            CoopCategories = "",
            OverflowServersJson = $"[{string.Join(",", overflows)}]"
        };

        private static ManageOverflow Updater(FakeDiscord discord, params Guild[] guilds) {
            var updater = FakeDiscord.Make<ManageOverflow>();
            FakeDiscord.Set(updater, "_client", discord.Hosted(guilds));
            FakeDiscord.Set(updater, "_logger", NullLogger<ManageOverflow>.Instance);
            FakeDiscord.Set(updater, "_provider", new ServiceCollection().BuildServiceProvider());
            return updater;
        }

        private static DbContextOptions<ApplicationDbContext> Options() =>
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

        private static async Task Seed(DbContextOptions<ApplicationDbContext> options, params DBUser[] users) {
            await using var db = new ApplicationDbContext(options);
            db.DBUsers.AddRange(users);
            await db.SaveChangesAsync();
        }

        private static DBUser Member(ulong discordId, ulong guildId = Main, ulong? lastGuild = null) =>
            new() { Id = Guid.NewGuid(), DiscordId = discordId, DiscordUsername = $"user{discordId}", GuildId = guildId, LastGuild = lastGuild };

        private static async Task<Dictionary<ulong, (ulong GuildId, ulong? LastGuild)>> Stored(DbContextOptions<ApplicationDbContext> options) {
            await using var db = new ApplicationDbContext(options);
            return await db.DBUsers.AsNoTracking().ToDictionaryAsync(x => x.DiscordId, x => (x.GuildId, x.LastGuild));
        }

        private async Task Track(DbContextOptions<ApplicationDbContext> options, ManageOverflow updater, params Guild[] guilds) {
            await using var db = new ApplicationDbContext(options);
            await updater.HandleMemberTracking(db, [.. guilds], TestContext!.CancellationToken);
        }

        private static List<string> Endpoints(FakeDiscord discord) => [.. discord.Writes.Select(x => string.Join(' ', x.Split(' ').Take(2)))];

        [TestMethod]
        public async Task MemberTracking_RestConfirmedDepartureClearsGuildAndRecordsLastGuild() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            var options = Options();
            await Seed(options, Member(1), Member(2));

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            var stored = await Stored(options);
            Assert.AreEqual((Main, (ulong?)null), stored[1]);
            Assert.AreEqual((0UL, (ulong?)Main), stored[2]);
        }

        [TestMethod]
        public async Task MemberTracking_CacheMissThatRestStillSeesIsNotADeparture() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            discord.RestMember(Main, 2);
            var options = Options();
            await Seed(options, Member(1), Member(2));

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            Assert.AreEqual((Main, (ulong?)null), (await Stored(options))[2]);
            CollectionAssert.Contains(discord.Calls, $"GET guilds/{Main}/members/2");
        }

        [TestMethod]
        public async Task MemberTracking_CacheSpikeOnlyPurgesTheRestConfirmedFew() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            var users = Enumerable.Range(2, 40).Select(i => Member((ulong)i)).ToList();
            foreach(var user in users.Skip(1))
                discord.RestMember(Main, user.DiscordId);
            var options = Options();
            await Seed(options, [Member(1), .. users]);

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            var departed = (await Stored(options)).Where(x => x.Value.GuildId == 0).Select(x => x.Key).ToList();
            CollectionAssert.AreEqual(new[] { 2UL }, departed);
        }

        [TestMethod]
        public async Task MemberTracking_RealMassExodusIsNotCappedByPercentage() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            var leavers = Enumerable.Range(2, 40).Select(i => Member((ulong)i)).ToArray();
            var options = Options();
            await Seed(options, [Member(1), .. leavers]);

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            var stored = await Stored(options);
            Assert.AreEqual(40, stored.Count(x => x.Value == (0UL, (ulong?)Main)));
            Assert.AreEqual(Main, stored[1].GuildId);
        }

        [TestMethod]
        public async Task MemberTracking_IncompleteMemberDownloadSkipsDeparturesAndRest() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            FakeDiscord.SetMemberCounts(main, 500, 1);
            var options = Options();
            await Seed(options, [.. Enumerable.Range(1, 30).Select(i => Member((ulong)i))]);

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            Assert.IsTrue((await Stored(options)).Values.All(x => x.GuildId == Main));
            Assert.IsFalse(discord.Calls.Any(x => x.Contains("/members/")));
        }

        [TestMethod]
        public async Task MemberTracking_EmptyMemberCacheSkipsDepartures() {
            var discord = new FakeDiscord();
            discord.Guild(Main);
            var options = Options();
            await Seed(options, Member(1), Member(2));

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            Assert.IsTrue((await Stored(options)).Values.All(x => x.GuildId == Main));
            Assert.IsFalse(discord.Calls.Any(x => x.Contains("/members/")));
        }

        [TestMethod]
        public async Task MemberTracking_IncompleteDownloadStillReassociatesRestConfirmedReturns() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            discord.User(main, 2);
            FakeDiscord.SetMemberCounts(main, 500, 2);
            var options = Options();
            await Seed(options, Member(1, 0, Main), Member(2, 0));

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            var stored = await Stored(options);
            Assert.AreEqual((Main, (ulong?)Main), stored[1]);
            Assert.AreEqual((Main, (ulong?)null), stored[2]);
        }

        [TestMethod]
        public async Task MemberTracking_StaleCacheReturnIsNotReassociatedWithoutRest() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            discord.User(main, 2, restKnown: false);
            var options = Options();
            await Seed(options, Member(1), Member(2, 0, Main));

            await Track(options, Updater(discord, DbGuild()), DbGuild());

            Assert.AreEqual((0UL, (ulong?)Main), (await Stored(options))[2]);
        }

        [TestMethod]
        public async Task MemberTracking_LeavesOtherGuildsMembersAndUnknownServersAlone() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            var options = Options();
            await Seed(options, Member(1), Member(2, OtherGuild), Member(3, OtherGuild));

            await Track(options, Updater(discord, DbGuild(), DbGuild(OtherGuild)), DbGuild(), DbGuild(OtherGuild));

            var stored = await Stored(options);
            Assert.AreEqual(OtherGuild, stored[2].GuildId);
            Assert.AreEqual(OtherGuild, stored[3].GuildId);
            Assert.IsFalse(discord.Calls.Any(x => x.Contains($"guilds/{OtherGuild}")));
        }

        [TestMethod]
        public async Task MemberTracking_CancelledBeforeStartTouchesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.User(main, 1);
            var options = Options();
            await Seed(options, Member(1), Member(2));

            await using(var db = new ApplicationDbContext(options))
                await Updater(discord, DbGuild()).HandleMemberTracking(db, [DbGuild()], new CancellationToken(true));

            Assert.AreEqual(Main, (await Stored(options))[2].GuildId);
        }

        private static (FakeDiscord Discord, SocketGuild Main, SocketGuild Overflow, SocketGuild Overflow2) RoleWorld() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, KnownRoles.Overflow, "Overflow");
            discord.Role(main, KnownRoles.Registered, "Registered");
            discord.Role(main, MemberRole, "Member");
            return (discord, main, discord.Guild(Overflow), discord.Guild(Overflow2));
        }

        [TestMethod]
        public async Task OverflowRole_AddedAndRemovedByMastersRules() {
            var (discord, main, overflow, overflow2) = RoleWorld();
            discord.User(main, 1, [KnownRoles.Registered, MemberRole]);
            discord.User(main, 2, [KnownRoles.Registered]);
            discord.User(main, 3, [KnownRoles.Registered, MemberRole, KnownRoles.Overflow]);
            discord.User(overflow, 3);
            discord.User(overflow2, 3);
            discord.User(main, 4, [MemberRole, KnownRoles.Overflow]);
            discord.User(main, 5, [KnownRoles.Registered, MemberRole, KnownRoles.Overflow]);
            discord.User(main, 6, [KnownRoles.Registered, MemberRole], isBot: true);
            discord.User(main, 7, [KnownRoles.Registered, MemberRole]);
            discord.User(overflow, 7);
            discord.User(overflow2, 7);
            discord.User(main, 8, [KnownRoles.Registered, MemberRole]);
            discord.User(overflow, 8);
            discord.User(main, 10, [KnownRoles.Overflow]);

            await Updater(discord, DbGuild(Main, Overflow, Overflow2)).ManageOverflowRoles(main, [overflow, overflow2], TestContext!.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] {
                    $"PUT guilds/{Main}/members/1/roles/{KnownRoles.Overflow}",
                    $"PUT guilds/{Main}/members/8/roles/{KnownRoles.Overflow}",
                    $"DELETE guilds/{Main}/members/3/roles/{KnownRoles.Overflow}",
                    $"DELETE guilds/{Main}/members/4/roles/{KnownRoles.Overflow}",
                    $"DELETE guilds/{Main}/members/10/roles/{KnownRoles.Overflow}"
                },
                Endpoints(discord));
        }

        [TestMethod]
        public async Task OverflowRole_MissingFromMainWritesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            discord.Role(main, KnownRoles.Registered, "Registered");
            discord.Role(main, MemberRole, "Member");
            var overflow = discord.Guild(Overflow);
            discord.User(main, 1, [KnownRoles.Registered, MemberRole]);

            await Updater(discord, DbGuild(Main, Overflow)).ManageOverflowRoles(main, [overflow], TestContext!.CancellationToken);

            Assert.AreEqual(0, discord.Writes.Count());
        }

        [TestMethod]
        public async Task OverflowRole_CancelledWritesNothing() {
            var (discord, main, overflow, _) = RoleWorld();
            discord.User(main, 1, [KnownRoles.Registered, MemberRole]);

            await Updater(discord, DbGuild(Main, Overflow)).ManageOverflowRoles(main, [overflow], new CancellationToken(true));

            Assert.AreEqual(0, discord.Writes.Count());
        }

        [TestMethod]
        public async Task OverflowMembers_KicksHumansMissingFromMainAndSyncsNicknames() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var overflow = discord.Guild(Overflow, ownerId: OverflowOwner);
            discord.User(main, 1, nickname: "Alice");
            discord.User(overflow, 1, nickname: "old");
            discord.User(main, 2, nickname: "Same");
            discord.User(overflow, 2, nickname: "Same");
            discord.User(overflow, 3, nickname: "gone");
            discord.User(overflow, 4, isBot: true);
            discord.User(main, OverflowOwner, nickname: "Boss");
            discord.User(overflow, OverflowOwner, nickname: "owner");
            discord.User(main, 5, nickname: "Robo", isBot: true);
            discord.User(overflow, 5, nickname: "bot", isBot: true);
            discord.User(main, 6);
            discord.User(overflow, 6, nickname: "stale");

            await Updater(discord, DbGuild(Main, Overflow)).ManageOverflowMembers(main, [overflow], TestContext!.CancellationToken);

            CollectionAssert.AreEquivalent(
                new[] {
                    $"DELETE guilds/{Overflow}/members/3",
                    $"PATCH guilds/{Overflow}/members/1",
                    $"PATCH guilds/{Overflow}/members/6"
                },
                Endpoints(discord));
            StringAssert.Contains(discord.Writes.Single(x => x.StartsWith($"PATCH guilds/{Overflow}/members/1")), "\"nick\":\"Alice\"");
        }

        [TestMethod]
        public async Task OverflowMembers_IncompleteMainDownloadSkipsKicksButStillSyncsNicknames() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var overflow = discord.Guild(Overflow);
            discord.User(main, 1, nickname: "Alice");
            discord.User(overflow, 1, nickname: "old");
            discord.User(overflow, 3);
            FakeDiscord.SetMemberCounts(main, 500, 1);

            await Updater(discord, DbGuild(Main, Overflow)).ManageOverflowMembers(main, [overflow], TestContext!.CancellationToken);

            CollectionAssert.AreEqual(new[] { $"PATCH guilds/{Overflow}/members/1" }, Endpoints(discord));
        }

        [TestMethod]
        public async Task OverflowMembers_EachOverflowIsCheckedAgainstMainIndependently() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var overflow = discord.Guild(Overflow);
            var overflow2 = discord.Guild(Overflow2);
            discord.User(main, 1);
            discord.User(overflow, 1);
            discord.User(overflow2, 2);

            await Updater(discord, DbGuild(Main, Overflow, Overflow2)).ManageOverflowMembers(main, [overflow, overflow2], TestContext!.CancellationToken);

            CollectionAssert.AreEqual(new[] { $"DELETE guilds/{Overflow2}/members/2" }, Endpoints(discord));
        }

        [TestMethod]
        public async Task OverflowMembers_CancelledWritesNothing() {
            var discord = new FakeDiscord();
            var main = discord.Guild(Main);
            var overflow = discord.Guild(Overflow);
            discord.User(overflow, 3);

            await Updater(discord, DbGuild(Main, Overflow)).ManageOverflowMembers(main, [overflow], new CancellationToken(true));

            Assert.AreEqual(0, discord.Writes.Count());
        }

        public TestContext? TestContext { get; set; }
    }
}
