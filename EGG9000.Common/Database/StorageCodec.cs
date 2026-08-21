using MessagePack;

using System;
using System.IO;
using System.IO.Compression;

namespace EGG9000.Common.Database {
    public static class StorageCodec {
        public static bool GZipWriteEnabled { get; set; }

        private static readonly MessagePackSerializerOptions PlainOptions = StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        static StorageCodec() {
            var raw = Environment.GetEnvironmentVariable("EGG9000_STORAGE_GZIP");
            GZipWriteEnabled = string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static byte[] Pack<T>(T value) {
            if(!GZipWriteEnabled)
                return MessagePackSerializer.Serialize(value, StorageMessagePack.Options);
            var plain = MessagePackSerializer.Serialize(value, PlainOptions);
            using var output = new MemoryStream();
            using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
                gzip.Write(plain, 0, plain.Length);
            return output.ToArray();
        }

        public static T Unpack<T>(byte[] stored) {
            if(stored is { Length: >= 2 } && stored[0] == 0x1F && stored[1] == 0x8B) {
                try {
                    using var input = new MemoryStream(stored, writable: false);
                    using var gzip = new GZipStream(input, CompressionMode.Decompress);
                    return MessagePackSerializer.Deserialize<T>(gzip, PlainOptions);
                } catch(MessagePackSerializationException) {
                    throw;
                } catch(Exception e) {
                    throw new MessagePackSerializationException("Failed to read gzip storage payload.", e);
                }
            }
            return MessagePackSerializer.Deserialize<T>(stored, StorageMessagePack.Options);
        }
    }
}
