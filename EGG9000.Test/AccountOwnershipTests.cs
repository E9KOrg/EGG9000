using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class AccountOwnershipTests {
        private static AccountOwnership.Row BlobRow(ulong discordId, params string[] ids) {
            var user = new DBUser { Id = Guid.NewGuid(), DiscordId = discordId };
            var accounts = new List<EggIncAccount>();
            foreach(var id in ids) accounts.Add(new EggIncAccount { Id = id });
            user.EggIncAccounts = accounts;
            return new(user.Id, user.DiscordId, user._eggIncIds, user._contractRegistrationByte);
        }

        private static readonly HashSet<Guid> NoRemovals = [];

        [TestMethod]
        public void FindsOwnerInMessagePackBlob() {
            var rows = new[] { BlobRow(1, "EI1"), BlobRow(2, "EI2", "EI3") };
            var owner = AccountOwnership.FindOwner(rows, NoRemovals, "EI3");
            Assert.AreEqual(2UL, owner!.Value.DiscordId);
        }

        [TestMethod]
        public void FindsOwnerInLegacyJsonColumn() {
            var rows = new[] { new AccountOwnership.Row(Guid.NewGuid(), 7, "[{\"Id\":\"EI9\"}]", null) };
            var owner = AccountOwnership.FindOwner(rows, NoRemovals, "ei9");
            Assert.AreEqual(7UL, owner!.Value.DiscordId);
        }

        [TestMethod]
        public void SkipsOwnerWhoseRemovalIsRecorded() {
            var stale = BlobRow(1, "EI5");
            var rows = new[] { stale, BlobRow(2, "EI6") };
            var owner = AccountOwnership.FindOwner(rows, new HashSet<Guid> { stale.Id }, "EI5");
            Assert.IsNull(owner);
        }

        [TestMethod]
        public void NullWhenNobodyOwnsIt() {
            var rows = new[] { BlobRow(1, "EI1") };
            Assert.IsNull(AccountOwnership.FindOwner(rows, NoRemovals, "EI404"));
        }

        [TestMethod]
        public void AccountWithoutIdDoesNotThrow() {
            var rows = new[] { BlobRow(1, [null!, "EI1"]) };
            var owner = AccountOwnership.FindOwner(rows, NoRemovals, "EI1");
            Assert.AreEqual(1UL, owner!.Value.DiscordId);
        }
    }
}
