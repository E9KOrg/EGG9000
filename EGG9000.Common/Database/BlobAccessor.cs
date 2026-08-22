using MessagePack;

using Newtonsoft.Json;

using System;

namespace EGG9000.Common.Database {
    public abstract class BlobAccessor<T, TStored>(IBlobCodec<T, TStored> codec) where T : class where TStored : class {
        private readonly IBlobCodec<T, TStored> _codec = codec;
        private T _cache;

        protected abstract bool IsMissing(TStored stored);
        protected abstract bool Unchanged(TStored existing, TStored encoded);
        protected virtual T MissingFallback() => null;
        protected virtual T ParsedNullFallback() => null;

        public T Get(TStored stored) {
            if(_cache != null) return _cache;
            if(IsMissing(stored)) {
                _cache = MissingFallback();
                return _cache;
            }
            _cache = _codec.Decode(stored) ?? ParsedNullFallback();
            return _cache;
        }

        public TStored Set(T value, TStored existing) {
            _cache = value;
            var encoded = _codec.Encode(value);
            if(existing != null && Unchanged(existing, encoded)) return existing;
            return encoded;
        }

        public void Prime(T value) {
            _cache = value;
        }
    }

    public abstract class ByteBlobAccessor<T>(IBlobCodec<T, byte[]> codec) : BlobAccessor<T, byte[]>(codec) where T : class {
        protected override bool IsMissing(byte[] stored) => stored is null or { Length: 0 };
        protected override bool Unchanged(byte[] existing, byte[] encoded) => existing.AsSpan().SequenceEqual(encoded);
    }

    public abstract class StringBlobAccessor<T>(IBlobCodec<T, string> codec) : BlobAccessor<T, string>(codec) where T : class {
        protected override bool IsMissing(string stored) => stored == null;
        protected override bool Unchanged(string existing, string encoded) => existing == encoded;
    }

    public sealed class MessagePackBlobAccessor<T>(MessagePackSerializerOptions options = null, Func<T> whenNull = null) : ByteBlobAccessor<T>(new MessagePackBlobCodec<T>(options)) where T : class {
        private readonly Func<T> _whenNull = whenNull;

        protected override T MissingFallback() => _whenNull?.Invoke();
        protected override T ParsedNullFallback() => _whenNull?.Invoke();
    }

    public sealed class CodecBlobAccessor<T>(Func<byte[], T> decode, Func<T, byte[]> encode) : ByteBlobAccessor<T>(new DelegateBlobCodec<T>(decode, encode)) where T : class;

    public sealed class JsonBlobAccessor<T>(string nullFallbackJson = null, Func<T> whenParsedNull = null) : StringBlobAccessor<T>(new JsonBlobCodec<T>()) where T : class {
        private readonly string _nullFallbackJson = nullFallbackJson;
        private readonly Func<T> _whenParsedNull = whenParsedNull;

        protected override T MissingFallback() {
            if(_nullFallbackJson == null) return null;
            return JsonConvert.DeserializeObject<T>(_nullFallbackJson) ?? _whenParsedNull?.Invoke();
        }

        protected override T ParsedNullFallback() => _whenParsedNull?.Invoke();
    }
}
