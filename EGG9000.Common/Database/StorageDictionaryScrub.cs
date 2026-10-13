using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Database {
    public static class StorageDictionaryScrub {
        public const int MinDigitRun = 12;
        public const int MinHexRun = 32;
        public const byte Filler = (byte)'x';

        public static byte[] Scrub(byte[] sample) {
            ArgumentNullException.ThrowIfNull(sample);
            var output = (byte[])sample.Clone();
            foreach(var (start, length) in Identifiers(sample))
                output.AsSpan(start, length).Fill(Filler);
            return output;
        }

        public static bool ContainsIdentifier(byte[] bytes) {
            ArgumentNullException.ThrowIfNull(bytes);
            return Identifiers(bytes).Any();
        }

        private static IEnumerable<(int Start, int Length)> Identifiers(byte[] bytes) {
            foreach(var (start, length) in Runs(bytes, 0, bytes.Length, IsHex)) {
                if(length >= MinHexRun) {
                    yield return (start, length);
                    continue;
                }
                foreach(var digits in Runs(bytes, start, start + length, IsDigit).Where(d => d.Length >= MinDigitRun || IsEiPrefixed(bytes, d.Start)))
                    yield return digits;
            }
        }

        private static IEnumerable<(int Start, int Length)> Runs(byte[] bytes, int from, int to, Func<byte, bool> predicate) {
            var i = from;
            while(i < to) {
                var end = i;
                while(end < to && predicate(bytes[end]))
                    end++;
                if(end > i)
                    yield return (i, end - i);
                i = end + 1;
            }
        }

        private static bool IsEiPrefixed(byte[] bytes, int start) {
            return start >= 2 && bytes[start - 2] is (byte)'E' or (byte)'e' && bytes[start - 1] is (byte)'I' or (byte)'i';
        }

        private static bool IsDigit(byte b) {
            return b is >= (byte)'0' and <= (byte)'9';
        }

        private static bool IsHex(byte b) {
            return IsDigit(b) || b is >= (byte)'a' and <= (byte)'f' or >= (byte)'A' and <= (byte)'F';
        }
    }
}
