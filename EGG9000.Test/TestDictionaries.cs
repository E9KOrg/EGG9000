using EGG9000.Common.Database;

using System.Linq;
using System.Text;

using ZstdSharp;

namespace EGG9000.Test {
    internal static class TestDictionaries {
        public const byte AccountsId = 240;
        public const byte CoopStatusId = 241;

        public static readonly StorageDictionary Accounts = StorageDictionary.Register(AccountsId, "test-accounts", Train(0));
        public static readonly StorageDictionary CoopStatus = StorageDictionary.Register(CoopStatusId, "test-coopstatus", Train(1000));

        public static StorageCompressionStrategy Strategy(StorageDictionary dictionary) {
            return new(StorageCompressionAlgorithm.Zstd, dictionary: dictionary);
        }

        private static byte[] Train(int seed) {
            var samples = Enumerable.Range(seed, 300)
                .Select(i => Encoding.UTF8.GetBytes($"egg-inc-coop-contract-{i % 13} farmer-{i % 29} group-{i % 5} eb {i * 7919 % 100000}"))
                .ToList();
            return DictBuilder.TrainFromBuffer(samples, 8192);
        }
    }
}
