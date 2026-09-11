using System;
using System.Collections.Concurrent;

using ZstdSharp;

namespace EGG9000.Common.Database {
    internal static class StorageZstd {
        private static readonly ConcurrentDictionary<(int Level, byte Dictionary), ConcurrentBag<Compressor>> Compressors = new();
        private static readonly ConcurrentDictionary<byte, ConcurrentBag<Decompressor>> Decompressors = new();

        public static byte[] Compress(byte[] plain, int level, StorageDictionary dictionary) {
            var pool = Compressors.GetOrAdd((level, dictionary?.Id ?? StorageDictionary.None), _ => []);
            if(!pool.TryTake(out var compressor)) {
                compressor = new Compressor(level);
                if(dictionary is not null)
                    compressor.LoadDictionary(dictionary.Bytes);
            }
            try {
                return compressor.Wrap(plain).ToArray();
            } finally {
                pool.Add(compressor);
            }
        }

        public static byte[] Decompress(ReadOnlySpan<byte> frame, byte dictionaryId) {
            var pool = Decompressors.GetOrAdd(dictionaryId, _ => []);
            if(!pool.TryTake(out var decompressor)) {
                decompressor = new Decompressor();
                if(dictionaryId != StorageDictionary.None)
                    decompressor.LoadDictionary(StorageDictionary.Get(dictionaryId).Bytes);
            }
            try {
                return decompressor.Unwrap(frame).ToArray();
            } finally {
                pool.Add(decompressor);
            }
        }
    }
}
