using MessagePack;

using Newtonsoft.Json;

using System;

namespace EGG9000.Common.Database {
    public interface IBlobCodec<T, TStored> {
        T Decode(TStored stored);
        TStored Encode(T value);
    }

    public sealed class MessagePackBlobCodec<T>(MessagePackSerializerOptions options = null) : IBlobCodec<T, byte[]> {
        public T Decode(byte[] stored) {
            return MessagePackSerializer.Deserialize<T>(stored, options);
        }

        public byte[] Encode(T value) {
            return MessagePackSerializer.Serialize(value, options);
        }
    }

    public sealed class JsonBlobCodec<T> : IBlobCodec<T, string> {
        public T Decode(string stored) {
            return JsonConvert.DeserializeObject<T>(stored);
        }

        public string Encode(T value) {
            return JsonConvert.SerializeObject(value);
        }
    }

    public sealed class DelegateBlobCodec<T>(Func<byte[], T> decode, Func<T, byte[]> encode) : IBlobCodec<T, byte[]> {
        public T Decode(byte[] stored) {
            return decode(stored);
        }

        public byte[] Encode(T value) {
            return encode(value);
        }
    }
}
