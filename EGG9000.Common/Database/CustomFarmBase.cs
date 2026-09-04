using MessagePack;
using System;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace EGG9000.Common.Database {
    public abstract class CustomFarmBase {
        [IgnoreMember]
        [JsonIgnore]
        [XmlIgnore]
        [IgnoreDataMember]
        public Ei.LocalContract LocalContract {
            get {
                if(_localContract is null && LocalContractBytesStorage is { Length: > 0 })
                    _localContract = Ei.LocalContract.Parser.ParseFrom(LocalContractBytesStorage);
                return _localContract;
            }
        }
        private Ei.LocalContract _localContract;

        protected void InvalidateLocalContract() {
            _localContract = null;
        }

        protected abstract byte[] LocalContractBytesStorage { get; }

        protected abstract long TimeAcceptedUnix { get; }

        [IgnoreMember]
        public DateTimeOffset Started => DateTimeOffset.FromUnixTimeSeconds(TimeAcceptedUnix);
    }
}
