using EGG9000.Common.Database;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.IO;
using System.Linq;
using System.Text;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageDictionaryTests {
        [TestMethod]
        public void Registry_IdsAreUniqueAndNonZero() {
            var ids = StorageDictionary.Registry.Select(d => d.Id).ToList();
            CollectionAssert.AllItemsAreUnique(ids);
            Assert.IsFalse(ids.Contains(StorageDictionary.None));
        }

        [TestMethod]
        public void Get_UnknownId_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => StorageDictionary.Get(200));
        }

        [TestMethod]
        public void PinnedIds_NeverChange() {
            byte[] pinned = [StorageDictionary.Accounts1Id, StorageDictionary.CoopStatus1Id];
            CollectionAssert.AreEqual(new byte[] { 1, 2 }, pinned);
        }

        [TestMethod]
        public void Assembly_EmbedsNoDictionary() {
            Assert.IsFalse(typeof(StorageDictionary).Assembly.GetManifestResourceNames().Any(n => n.EndsWith(".zdict")));
        }

        [TestMethod]
        public void Scrub_MasksEidsDeviceIdsAndLongDigitRuns() {
            var plain = Encoding.ASCII.GetBytes("id EI6493007777366016 dev 0123456789abcdef0123456789abcdef n 123456789012 ei42 keep 12345");
            var scrubbed = StorageDictionaryScrub.Scrub(plain);
            var text = Encoding.ASCII.GetString(scrubbed);

            Assert.AreEqual(plain.Length, scrubbed.Length);
            Assert.IsFalse(StorageDictionaryScrub.ContainsIdentifier(scrubbed));
            Assert.IsFalse(text.Contains("6493007777366016"));
            Assert.IsFalse(text.Contains("0123456789abcdef"));
            Assert.IsFalse(text.Contains("123456789012"));
            Assert.IsTrue(text.Contains("eixx"));
            Assert.IsTrue(text.EndsWith("keep 12345"));
        }

        [TestMethod]
        public void ContainsIdentifier_FindsRawEid() {
            Assert.IsTrue(StorageDictionaryScrub.ContainsIdentifier(Encoding.ASCII.GetBytes("xxEI5223299518300160xx")));
            Assert.IsFalse(StorageDictionaryScrub.ContainsIdentifier(Encoding.ASCII.GetBytes("no ids here 2026")));
        }
    }
}
