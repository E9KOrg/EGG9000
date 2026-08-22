using System;
using System.IO;
using System.IO.Compression;

namespace EGG9000.Common.Database {
    public enum StorageCompressionAlgorithm : byte {
        Raw = 0x00,
        GZip = 0x01,
        Brotli = 0x02
    }

    public sealed class StorageCompressionStrategy(StorageCompressionAlgorithm algorithm, int brotliQuality = 6, int rawThreshold = 64) {
        public static readonly StorageCompressionStrategy AccountGraph = new(StorageCompressionAlgorithm.Brotli);
        public static readonly StorageCompressionStrategy CoopStatus = new(StorageCompressionAlgorithm.Brotli);

        public StorageCompressionAlgorithm Algorithm { get; } = algorithm;
        public int BrotliQuality { get; } = brotliQuality;
        public int RawThreshold { get; } = rawThreshold;
    }

    public static class StorageCompression {
        public const byte Marker = 0xEB;

        public static byte[] Compress(byte[] plain, StorageCompressionStrategy strategy) {
            ArgumentNullException.ThrowIfNull(plain);
            ArgumentNullException.ThrowIfNull(strategy);
            if(strategy.Algorithm == StorageCompressionAlgorithm.Raw || plain.Length <= strategy.RawThreshold)
                return Envelope(StorageCompressionAlgorithm.Raw, plain);
            var compressed = Encode(plain, strategy);
            return compressed.Length < plain.Length
                ? Envelope(strategy.Algorithm, compressed)
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
                _ => throw new InvalidDataException($"Unknown storage compression algorithm 0x{stored[1]:X2}.")
            };
        }

        private static byte[] Envelope(StorageCompressionAlgorithm algorithm, byte[] payload) {
            var output = new byte[payload.Length + 2];
            output[0] = Marker;
            output[1] = (byte)algorithm;
            payload.CopyTo(output, 2);
            return output;
        }

        private static byte[] Encode(byte[] plain, StorageCompressionStrategy strategy) {
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
    }
}
