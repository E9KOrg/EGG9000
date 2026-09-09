using System;
using System.IO;
using System.IO.Compression;

using ZstdSharp;

namespace EGG9000.Common.Database {
    public enum StorageCompressionAlgorithm : byte {
        Raw = 0x00,
        GZip = 0x01,
        Brotli = 0x02,
        Zstd = 0x03
    }

    public sealed class StorageCompressionStrategy(StorageCompressionAlgorithm algorithm, int brotliQuality = 6, int rawThreshold = 64, int zstdLevel = 9, StorageDictionary dictionary = null) {
        private static volatile StorageCompressionStrategy _accountGraph = new(StorageCompressionAlgorithm.Zstd, dictionary: StorageDictionary.Accounts1);
        private static volatile StorageCompressionStrategy _coopStatus = new(StorageCompressionAlgorithm.Zstd, dictionary: StorageDictionary.CoopStatus1);

        public static StorageCompressionStrategy AccountGraph => _accountGraph;
        public static StorageCompressionStrategy CoopStatus => _coopStatus;

        public static void SetAccountGraph(StorageDictionary dictionary) => _accountGraph = new(StorageCompressionAlgorithm.Zstd, dictionary: dictionary);
        public static void SetCoopStatus(StorageDictionary dictionary) => _coopStatus = new(StorageCompressionAlgorithm.Zstd, dictionary: dictionary);

        public StorageCompressionAlgorithm Algorithm { get; } = algorithm;
        public int BrotliQuality { get; } = brotliQuality;
        public int RawThreshold { get; } = rawThreshold;
        public int ZstdLevel { get; } = zstdLevel;
        public StorageDictionary Dictionary { get; } = dictionary;
        public byte DictionaryId => Dictionary?.Id ?? StorageDictionary.None;
        public int HeaderLength => StorageCompression.HeaderLength(Algorithm);
    }

    public static class StorageCompression {
        public const byte Marker = 0xEB;

        public static int HeaderLength(StorageCompressionAlgorithm algorithm) => algorithm == StorageCompressionAlgorithm.Zstd ? 3 : 2;

        public static byte[] Compress(byte[] plain, StorageCompressionStrategy strategy) {
            ArgumentNullException.ThrowIfNull(plain);
            ArgumentNullException.ThrowIfNull(strategy);
            if(strategy.Algorithm == StorageCompressionAlgorithm.Raw || plain.Length <= strategy.RawThreshold)
                return Envelope(StorageCompressionAlgorithm.Raw, plain);
            var compressed = Encode(plain, strategy);
            return compressed.Length + strategy.HeaderLength < plain.Length + HeaderLength(StorageCompressionAlgorithm.Raw)
                ? Envelope(strategy.Algorithm, compressed, strategy.DictionaryId)
                : Envelope(StorageCompressionAlgorithm.Raw, plain);
        }

        public static bool IsEnveloped(byte[] stored) => stored is { Length: >= 2 } && stored[0] == Marker;

        public static byte[] Decompress(byte[] stored) {
            if(!IsEnveloped(stored))
                throw new InvalidDataException("Payload is not a storage compression envelope.");
            return (StorageCompressionAlgorithm)stored[1] switch {
                StorageCompressionAlgorithm.Raw => stored.AsSpan(2).ToArray(),
                StorageCompressionAlgorithm.GZip => Decode(stored, s => new GZipStream(s, CompressionMode.Decompress)),
                StorageCompressionAlgorithm.Brotli => Decode(stored, s => new BrotliStream(s, CompressionMode.Decompress)),
                StorageCompressionAlgorithm.Zstd => DecodeZstd(stored),
                _ => throw new InvalidDataException($"Unknown storage compression algorithm 0x{stored[1]:X2}.")
            };
        }

        private static byte[] Envelope(StorageCompressionAlgorithm algorithm, byte[] payload, byte dictionaryId = StorageDictionary.None) {
            var header = HeaderLength(algorithm);
            var output = new byte[payload.Length + header];
            output[0] = Marker;
            output[1] = (byte)algorithm;
            if(header > 2)
                output[2] = dictionaryId;
            payload.CopyTo(output, header);
            return output;
        }

        private static byte[] Encode(byte[] plain, StorageCompressionStrategy strategy) {
            if(strategy.Algorithm == StorageCompressionAlgorithm.Zstd)
                return StorageZstd.Compress(plain, strategy.ZstdLevel, strategy.Dictionary);
            using var output = new MemoryStream();
            using(Stream stream = strategy.Algorithm switch {
                StorageCompressionAlgorithm.GZip => new GZipStream(output, CompressionLevel.Optimal),
                StorageCompressionAlgorithm.Brotli => new BrotliStream(output, new BrotliCompressionOptions { Quality = strategy.BrotliQuality }),
                _ => throw new InvalidDataException($"Storage compression algorithm {strategy.Algorithm} cannot encode.")
            })
                stream.Write(plain, 0, plain.Length);
            return output.ToArray();
        }

        private static byte[] Decode(byte[] stored, Func<Stream, Stream> wrap) {
            using var input = new MemoryStream(stored, 2, stored.Length - 2, writable: false);
            using var stream = wrap(input);
            using var output = new MemoryStream();
            stream.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecodeZstd(byte[] stored) {
            if(stored.Length < 3)
                throw new InvalidDataException("Zstd storage envelope is missing its dictionary byte.");
            try {
                return StorageZstd.Decompress(stored.AsSpan(3), stored[2]);
            } catch(ZstdException e) {
                throw new InvalidDataException($"Zstd storage payload could not be decoded with dictionary {stored[2]}.", e);
            }
        }
    }
}
