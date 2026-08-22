using MessagePack;

using Newtonsoft.Json;

using System;

namespace EGG9000.Common.Database {
    public interface IBlobCodec<T, TStored> {
        T Decode(TStored stored);
        TStored Encode(T value);
    }

    public sealed class MessagePackBlobCodec<T>(MessagePackSerializerOptions options = null) : IBlobCodec<T, byte[]> {
        private readonly MessagePackSerializerOptions _options = options;

        public T Decode(byte[] stored) => MessagePackSerializer.Deserialize<T>(stored, _options);
        public byte[] Encode(T value) => MessagePackSerializer.Serialize(value, _options);
    }

    public sealed class JsonBlobCodec<T> : IBlobCodec<T, string> {
        public T Decode(string stored) => JsonConvert.DeserializeObject<T>(stored);
        public string Encode(T value) => JsonConvert.SerializeObject(value);
    }

    public sealed class DelegateBlobCodec<T>(Func<byte[], T> decode, Func<T, byte[]> encode) : IBlobCodec<T, byte[]> {
        private readonly Func<byte[], T> _decode = decode;
        private readonly Func<T, byte[]> _encode = encode;

        public T Decode(byte[] stored) => _decode(stored);
        public byte[] Encode(T value) => _encode(value);
    }
}
