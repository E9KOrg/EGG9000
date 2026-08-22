using MessagePack;

using Newtonsoft.Json;

using System;

namespace EGG9000.Common.Database {
    public sealed class MessagePackBlobAccessor<T>(MessagePackSerializerOptions options = null, Func<T> whenNull = null) where T : class {
        private readonly MessagePackSerializerOptions _options = options;
        private readonly Func<T> _whenNull = whenNull;
        private T _cache;

        public T Get(byte[] stored) {
            if(_cache != null) return _cache;
            if(stored is null or { Length: 0 }) {
                if(_whenNull == null) return null;
                _cache = _whenNull();
                return _cache;
            }
            _cache = MessagePackSerializer.Deserialize<T>(stored, _options);
            if(_whenNull != null) _cache ??= _whenNull();
            return _cache;
        }

        public byte[] Set(T value, byte[] existing) {
            _cache = value;
            var encoded = MessagePackSerializer.Serialize(value, _options);
            if(existing != null && existing.AsSpan().SequenceEqual(encoded)) return existing;
            return encoded;
        }

        public void Prime(T value) {
            _cache = value;
        }
    }

    public sealed class JsonBlobAccessor<T>(string nullFallbackJson = null, Func<T> whenParsedNull = null) where T : class {
        private readonly string _nullFallbackJson = nullFallbackJson;
        private readonly Func<T> _whenParsedNull = whenParsedNull;
        private T _cache;

        public T Get(string stored) {
            if(_cache != null) return _cache;
            var source = stored ?? _nullFallbackJson;
            if(source == null) return null;
            _cache = JsonConvert.DeserializeObject<T>(source);
            if(_whenParsedNull != null) _cache ??= _whenParsedNull();
            return _cache;
        }

        public string Set(T value, string existing) {
            _cache = value;
            var encoded = JsonConvert.SerializeObject(value);
            if(encoded == existing) return existing;
            return encoded;
        }
    }

    public sealed class CodecBlobAccessor<T>(Func<byte[], T> decode, Func<T, byte[]> encode) where T : class {
        private readonly Func<byte[], T> _decode = decode;
        private readonly Func<T, byte[]> _encode = encode;
        private T _cache;

        public T Get(byte[] stored) {
            if(_cache != null) return _cache;
            if(stored == null) return null;
            _cache = _decode(stored);
            return _cache;
        }

        public byte[] Set(T value, byte[] existing) {
            _cache = value;
            var encoded = _encode(value);
            if(existing != null && existing.AsSpan().SequenceEqual(encoded)) return existing;
            return encoded;
        }
    }
}
