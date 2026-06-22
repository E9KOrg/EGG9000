using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Common.EggIncAPI {
    // Google.Protobuf hard-rejects string fields that aren't valid UTF-8 (throws "String is invalid
    // UTF-8."). Some Egg Inc backups carry invalid bytes in name fields (user_name, farm/coop names),
    // so ParseFrom blows up and the whole backup is lost - even though every tool built on the Go or
    // Python protobuf libraries reads it fine, because those libraries are lenient.
    //
    // This walks the raw wire bytes and replaces invalid UTF-8 bytes inside leaf length-delimited
    // fields with '?' (0x3F). The replacement is one byte for one byte, so every embedded length
    // prefix stays correct and the rest of the message decodes unchanged. Nested messages are
    // recursed into first, so only genuine string/bytes leaves get scrubbed.
    public static class ProtobufUtf8Sanitizer {

        public static bool IsInvalidUtf8(Exception e) =>
            e.Message?.Contains("invalid UTF-8", StringComparison.OrdinalIgnoreCase) == true;

        // Returns a copy of the wire bytes with invalid UTF-8 scrubbed out of length-delimited leaves.
        // On any structural surprise it leaves the offending region untouched rather than guessing.
        public static byte[] Sanitize(byte[] data) {
            if(data is null || data.Length == 0)
                return data;
            var copy = (byte[])data.Clone();
            SanitizeRange(copy, 0, copy.Length);
            return copy;
        }

        private static void SanitizeRange(byte[] buffer, int start, int end) {
            var pos = start;
            while(pos < end) {
                if(!TryReadVarint(buffer, ref pos, end, out var tag))
                    return;
                var wireType = (int)(tag & 0x7);
                switch(wireType) {
                    case 0:
                        if(!TryReadVarint(buffer, ref pos, end, out _))
                            return;
                        break;
                    case 1:
                        pos += 8;
                        if(pos > end) return;
                        break;
                    case 5:
                        pos += 4;
                        if(pos > end) return;
                        break;
                    case 2:
                        if(!TryReadVarint(buffer, ref pos, end, out var len))
                            return;
                        var fieldEnd = pos + (int)len;
                        if(len < 0 || fieldEnd > end)
                            return;
                        // Prefer treating the payload as a nested message: if it parses as well-formed
                        // wire-format we recurse so only its leaves are touched. Otherwise it is a
                        // scalar (string/bytes) leaf, so scrub any invalid UTF-8 in place.
                        if(LooksLikeMessage(buffer, pos, fieldEnd)) {
                            SanitizeRange(buffer, pos, fieldEnd);
                        } else {
                            ScrubInvalidUtf8(buffer, pos, fieldEnd);
                        }
                        pos = fieldEnd;
                        break;
                    default:
                        // Wire types 3/4 (groups) are deprecated and unused by Egg Inc; bail out safely.
                        return;
                }
            }
        }

        // A length-delimited payload counts as a nested message only if it fully consumes as valid
        // wire-format with at least one field. Empty payloads and scalars fall through to scrubbing.
        private static bool LooksLikeMessage(byte[] buffer, int start, int end) {
            if(start >= end)
                return false;
            var pos = start;
            var fields = 0;
            while(pos < end) {
                if(!TryReadVarint(buffer, ref pos, end, out var tag))
                    return false;
                var fieldNumber = tag >> 3;
                var wireType = (int)(tag & 0x7);
                if(fieldNumber == 0)
                    return false;
                switch(wireType) {
                    case 0:
                        if(!TryReadVarint(buffer, ref pos, end, out _))
                            return false;
                        break;
                    case 1:
                        pos += 8;
                        if(pos > end) return false;
                        break;
                    case 5:
                        pos += 4;
                        if(pos > end) return false;
                        break;
                    case 2:
                        if(!TryReadVarint(buffer, ref pos, end, out var len))
                            return false;
                        pos += (int)len;
                        if(len < 0 || pos > end) return false;
                        break;
                    default:
                        return false;
                }
                fields++;
            }
            return pos == end && fields > 0;
        }

        // Replace bytes that are not part of a valid UTF-8 sequence with '?', one byte for one byte.
        private static void ScrubInvalidUtf8(byte[] buffer, int start, int end) {
            var i = start;
            while(i < end) {
                var b = buffer[i];
                if(b < 0x80) {
                    i++;
                    continue;
                }
                var seqLen = b switch {
                    >= 0xC2 and <= 0xDF => 2,
                    >= 0xE0 and <= 0xEF => 3,
                    >= 0xF0 and <= 0xF4 => 4,
                    _ => 0
                };
                if(seqLen == 0 || i + seqLen > end || !ValidContinuation(buffer, i, seqLen)) {
                    buffer[i] = (byte)'?';
                    i++;
                    continue;
                }
                i += seqLen;
            }
        }

        private static bool ValidContinuation(byte[] buffer, int start, int seqLen) {
            for(var k = 1; k < seqLen; k++) {
                var c = buffer[start + k];
                if(c < 0x80 || c > 0xBF)
                    return false;
            }
            return true;
        }

        private static bool TryReadVarint(byte[] buffer, ref int pos, int end, out ulong value) {
            value = 0;
            var shift = 0;
            while(pos < end) {
                var b = buffer[pos++];
                value |= (ulong)(b & 0x7F) << shift;
                if((b & 0x80) == 0)
                    return true;
                shift += 7;
                if(shift >= 64)
                    return false;
            }
            return false;
        }
    }
}
