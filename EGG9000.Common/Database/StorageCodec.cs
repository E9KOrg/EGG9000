using MessagePack;

using System;
using System.IO;
using System.IO.Compression;

namespace EGG9000.Common.Database {
    public static class StorageCodec {
        public static bool CompressWriteEnabled { get; set; }

        private static readonly MessagePackSerializerOptions PlainOptions = StorageMessagePack.Options.WithCompression(MessagePackCompression.None);

        static StorageCodec() {
            var raw = Environment.GetEnvironmentVariable("EGG9000_STORAGE_COMPRESS");
            CompressWriteEnabled = string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static byte[] Pack<T>(T value) {
            if(!CompressWriteEnabled)
                return MessagePackSerializer.Serialize(value, StorageMessagePack.Options);
            var plain = MessagePackSerializer.Serialize(value, PlainOptions);
            return StorageCompression.Compress(plain, StorageCompressionStrategy.AccountGraph);
        }

        public static T Unpack<T>(byte[] stored) {
            if(StorageCompression.IsEnveloped(stored)) {
                try {
                    return MessagePackSerializer.Deserialize<T>(StorageCompression.Decompress(stored), PlainOptions);
                } catch(MessagePackSerializationException) {
                    throw;
                } catch(Exception e) {
                    throw new MessagePackSerializationException("Failed to read enveloped storage payload.", e);
                }
            }
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
