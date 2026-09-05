using EGG9000.Common.Database;
using MessagePack;
using System;
using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace EGG9000.ConvertProbe.Inspect {
    public static class BlobDecompressor {
        public sealed record Result(byte[] Plain, string Framing);

        public static Result ToPlainMessagePack(byte[] blob) {
            if(StorageCompression.IsEnveloped(blob))
                return new Result(StorageCompression.Decompress(blob), "envelope");
            if(blob is { Length: >= 2 } && blob[0] == 0x1F && blob[1] == 0x8B)
                return new Result(Gunzip(blob), "gzip");
            var reader = new MessagePackReader(blob);
            if(reader.NextMessagePackType == MessagePackType.Array) {
                var peek = reader.CreatePeekReader();
                var count = peek.ReadArrayHeader();
                if(count > 0 && peek.NextMessagePackType == MessagePackType.Extension) {
                    var header = peek.ReadExtensionFormatHeader();
                    if(header.TypeCode == ReservedExtensionTypeCodes.Lz4BlockArray)
                        return new Result(DecodeBlockArray(ref peek, count - 1), "lz4blockarray");
                }
            }
            if(reader.NextMessagePackType == MessagePackType.Extension) {
                var peek = reader.CreatePeekReader();
                var header = peek.ReadExtensionFormatHeader();
                if(header.TypeCode == ReservedExtensionTypeCodes.Lz4Block)
                    return new Result(DecodeSingleBlock(ref peek, header.Length), "lz4block");
            }
            return new Result(blob, "plain");
        }

        private static byte[] Gunzip(byte[] blob) {
            using var input = new MemoryStream(blob, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] DecodeBlockArray(ref MessagePackReader reader, int blocks) {
            var lengths = new int[blocks];
            var total = 0L;
            for(var i = 0; i < blocks; i++) {
                lengths[i] = reader.ReadInt32();
                total += lengths[i];
            }
            var plain = new byte[total];
            var offset = 0;
            for(var i = 0; i < blocks; i++) {
                var compressed = reader.ReadBytes() ?? throw new InvalidDataException("LZ4 block array entry is nil.");
                var written = Lz4.Decode(compressed.ToArray(), plain.AsSpan(offset, lengths[i]));
                if(written != lengths[i])
                    throw new InvalidDataException($"LZ4 block {i} decoded to {written} bytes, expected {lengths[i]}.");
                offset += written;
            }
            return plain;
        }

        private static byte[] DecodeSingleBlock(ref MessagePackReader reader, uint extLength) {
            var before = reader.Consumed;
            var uncompressedLength = reader.ReadInt32();
            var lengthPrefix = (int)(reader.Consumed - before);
            var compressed = reader.ReadRaw((long)extLength - lengthPrefix).ToArray();
            var plain = new byte[uncompressedLength];
            var written = Lz4.Decode(compressed, plain);
            if(written != uncompressedLength)
                throw new InvalidDataException($"LZ4 block decoded to {written} bytes, expected {uncompressedLength}.");
            return plain;
        }

        private static class Lz4 {
            public static int Decode(ReadOnlySpan<byte> src, Span<byte> dst) {
                var s = 0;
                var d = 0;
                while(s < src.Length) {
                    var token = src[s++];
                    var literal = token >> 4;
                    if(literal == 15) {
                        byte more;
                        do {
                            more = src[s++];
                            literal += more;
                        } while(more == 255);
                    }
                    src.Slice(s, literal).CopyTo(dst.Slice(d));
                    s += literal;
                    d += literal;
                    if(s >= src.Length) break;
                    var offset = src[s] | (src[s + 1] << 8);
                    s += 2;
                    if(offset == 0 || offset > d)
                        throw new InvalidDataException($"LZ4 match offset {offset} out of range at output {d}.");
                    var match = token & 15;
                    if(match == 15) {
                        byte more;
                        do {
                            more = src[s++];
                            match += more;
                        } while(more == 255);
                    }
                    match += 4;
                    var from = d - offset;
                    for(var i = 0; i < match; i++)
                        dst[d++] = dst[from++];
                }
                return d;
            }
        }
    }
}
