using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.IO;

namespace EGG9000.Bot.Automated {
    public enum SweepOutcomeKind {
        Converted,
        Current,
        Failed
    }

    public readonly record struct SweepOutcome(SweepOutcomeKind Kind, byte[] Bytes, Exception Error) {
        public static readonly SweepOutcome Current = new(SweepOutcomeKind.Current, null, null);

        public static SweepOutcome Converted(byte[] bytes) => new(SweepOutcomeKind.Converted, bytes, null);

        public static SweepOutcome Failed(Exception error) => new(SweepOutcomeKind.Failed, null, error);
    }

    public static class StorageSweepCodec {
        public static SweepOutcome Accounts(byte[] stored) {
            byte[] packed;
            try {
                var accounts = StorageCodec.Unpack<List<EggIncAccount>>(stored);
                if(accounts is null)
                    return SweepOutcome.Failed(new InvalidDataException("Accounts blob decoded to null."));
                packed = StorageCodec.Pack(accounts);
            } catch(Exception e) {
                return SweepOutcome.Failed(e);
            }
            return Compare(stored, packed);
        }

        public static SweepOutcome CoopStatus(byte[] stored) {
            byte[] packed;
            try {
                var status = CoopStatusCodec.Decode(stored);
                if(status is null)
                    return SweepOutcome.Failed(new InvalidDataException("Coop status blob decoded to null."));
                packed = CoopStatusCodec.Encode(status);
            } catch(Exception e) {
                return SweepOutcome.Failed(e);
            }
            return Compare(stored, packed);
        }

        private static SweepOutcome Compare(byte[] stored, byte[] packed) {
            if(packed is null)
                return SweepOutcome.Failed(new InvalidDataException("Re-encode produced no bytes."));
            return packed.AsSpan().SequenceEqual(stored) ? SweepOutcome.Current : SweepOutcome.Converted(packed);
        }
    }
}
