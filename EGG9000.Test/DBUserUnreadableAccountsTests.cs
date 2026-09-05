using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class DBUserUnreadableAccountsTests {
        private static readonly byte[] Corrupt = [0xC1, 0xFF, 0x00];

        private static DBUser Unreadable() => new() { DiscordId = 42, Usernames = "keep-me", EIDs = "EI0000000000000001", _contractRegistrationByte = [.. Corrupt] };

        [TestMethod]
        public void Getter_FlagsUnreadable_ReturnsEmpty_LeavesColumn() {
            var user = Unreadable();
            var accounts = user.EggIncAccounts;
            Assert.AreEqual(0, accounts.Count);
            Assert.IsTrue(user.AccountsUnreadable);
            CollectionAssert.AreEqual(Corrupt, user._contractRegistrationByte);
        }

        [TestMethod]
        public void UpdateAccounts_EmptyOverUnreadable_Refused() {
            var user = Unreadable();
            _ = user.EggIncAccounts;
            var changed = user.UpdateAccounts();
            Assert.IsFalse(changed);
            Assert.IsTrue(user.AccountsUnreadable);
            CollectionAssert.AreEqual(Corrupt, user._contractRegistrationByte);
            Assert.AreEqual("keep-me", user.Usernames);
            Assert.AreEqual("EI0000000000000001", user.EIDs);
        }

        [TestMethod]
        public void Setter_EmptyOverUnreadable_Refused() {
            var user = Unreadable();
            _ = user.EggIncAccounts;
            user.EggIncAccounts = [];
            CollectionAssert.AreEqual(Corrupt, user._contractRegistrationByte);
            Assert.IsTrue(user.AccountsUnreadable);
        }

        [TestMethod]
        public void RemoveID_OnUnreadable_LeavesColumn() {
            var user = Unreadable();
            user.RemoveID("EI0000000000000001");
            CollectionAssert.AreEqual(Corrupt, user._contractRegistrationByte);
            Assert.IsTrue(user.AccountsUnreadable);
        }

        [TestMethod]
        public void AddingAccount_OverUnreadable_WritesAndClearsFlag() {
            var user = Unreadable();
            user.EggIncAccounts.Add(new EggIncAccount { Id = "EI0000000000000002" });
            var changed = user.UpdateAccounts();
            Assert.IsTrue(changed);
            Assert.IsFalse(user.AccountsUnreadable);
            var rehydrated = StorageCodec.Unpack<List<EggIncAccount>>(user._contractRegistrationByte);
            Assert.AreEqual(1, rehydrated.Count);
            Assert.AreEqual("EI0000000000000002", rehydrated[0].Id);
        }

        [TestMethod]
        public void RemoveID_LastAccountOnReadableUser_StillClears() {
            var user = new DBUser { DiscordId = 7 };
            user.EggIncAccounts = [new EggIncAccount { Id = "EI0000000000000003" }];
            user.RemoveID("EI0000000000000003");
            Assert.IsFalse(user.AccountsUnreadable);
            var rehydrated = StorageCodec.Unpack<List<EggIncAccount>>(user._contractRegistrationByte);
            Assert.AreEqual(0, rehydrated.Count);
        }

        [TestMethod]
        public void ReadableUser_NeverFlagged() {
            var user = new DBUser { DiscordId = 8 };
            user.EggIncAccounts = [new EggIncAccount { Id = "EI0000000000000004" }];
            var rehydrated = new DBUser { _contractRegistrationByte = user._contractRegistrationByte };
            Assert.AreEqual(1, rehydrated.EggIncAccounts.Count);
            Assert.IsFalse(rehydrated.AccountsUnreadable);
        }
    }
}
