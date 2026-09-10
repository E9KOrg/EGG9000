using MessagePack;

using Newtonsoft.Json;

using System;

namespace EGG9000.Common.Database {
    public abstract class BlobAccessor<T, TStored>(IBlobCodec<T, TStored> codec) where T : class where TStored : class {
        private T _cache;

        protected abstract bool IsMissing(TStored stored);
        protected abstract bool Unchanged(TStored existing, TStored encoded);
        protected virtual T MissingFallback() {
            return null;
        }

        protected virtual T ParsedNullFallback() {
            return null;
        }

        public T Get(TStored stored) {
            if(_cache != null) return _cache;
            if(IsMissing(stored)) {
                _cache = MissingFallback();
                return _cache;
            }
            _cache = codec.Decode(stored) ?? ParsedNullFallback();
            return _cache;
        }

        public TStored Set(T value, TStored existing) {
            _cache = value;
            var encoded = codec.Encode(value);
            return existing != null && Unchanged(existing, encoded) ? existing : encoded;
        }

        public void Prime(T value) {
            _cache = value;
        }
    }

    public abstract class ByteBlobAccessor<T>(IBlobCodec<T, byte[]> codec) : BlobAccessor<T, byte[]>(codec) where T : class {
        protected override bool IsMissing(byte[] stored) {
            return stored is null or { Length: 0 };
        }

        protected override bool Unchanged(byte[] existing, byte[] encoded) {
            return existing.AsSpan().SequenceEqual(encoded);
        }
    }

    public abstract class StringBlobAccessor<T>(IBlobCodec<T, string> codec) : BlobAccessor<T, string>(codec) where T : class {
        protected override bool IsMissing(string stored) {
            return stored == null;
        }

        protected override bool Unchanged(string existing, string encoded) {
            return existing == encoded;
        }
    }

    public sealed class MessagePackBlobAccessor<T>(MessagePackSerializerOptions options = null, Func<T> whenNull = null) : ByteBlobAccessor<T>(new MessagePackBlobCodec<T>(options)) where T : class {
        protected override T MissingFallback() {
            return whenNull?.Invoke();
        }

        protected override T ParsedNullFallback() {
            return whenNull?.Invoke();
        }
    }

    public sealed class CodecBlobAccessor<T>(Func<byte[], T> decode, Func<T, byte[]> encode) : ByteBlobAccessor<T>(new DelegateBlobCodec<T>(decode, encode)) where T : class;

    public sealed class JsonBlobAccessor<T>(string nullFallbackJson = null, Func<T> whenParsedNull = null) : StringBlobAccessor<T>(new JsonBlobCodec<T>()) where T : class {
        protected override T MissingFallback() {
            return nullFallbackJson == null ? null : JsonConvert.DeserializeObject<T>(nullFallbackJson) ?? whenParsedNull?.Invoke();
        }

        protected override T ParsedNullFallback() {
            return whenParsedNull?.Invoke();
        }
    }
}
