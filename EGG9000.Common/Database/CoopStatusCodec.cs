using EGG9000.Common.Helpers;

using Google.Protobuf;

using Newtonsoft.Json;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EGG9000.Common.Database {
    public static class CoopStatusCodec {
        private const byte ProtoMarker = 0xE9;

        public static bool ProtoWriteEnabled { get; set; }

        static CoopStatusCodec() {
            var raw = Environment.GetEnvironmentVariable("EGG9000_COOPSTATUS_PROTO");
            ProtoWriteEnabled = string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static byte[] Encode(Ei.ContractCoopStatusResponse status) {
            if(!ProtoWriteEnabled || status == null)
                return EncodeLegacy(status);
            return StorageCompression.Compress(status.ToByteArray(), StorageCompressionStrategy.CoopStatus);
        }

        private static byte[] EncodeLegacy(Ei.ContractCoopStatusResponse status) {
            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(status, new JsonSerializerSettings { ContractResolver = new CustomContractResolver() }));
            using var output = new MemoryStream();
            using(var gzip = new GZipStream(output, CompressionMode.Compress))
                gzip.Write(bytes, 0, bytes.Length);
            return output.ToArray();
        }

        public static Ei.ContractCoopStatusResponse Decode(byte[] stored) {
            if(stored == null)
                return null;
            if(stored is { Length: >= 2 } && stored[0] == 0x1F && stored[1] == 0x8B)
                return DecodeLegacy(stored);
            if(stored is { Length: >= 2 } && stored[0] == ProtoMarker)
                return DecodeProto(stored);
            if(StorageCompression.IsEnveloped(stored))
                return WithRecomputedTimeLeft(Ei.ContractCoopStatusResponse.Parser.ParseFrom(StorageCompression.Decompress(stored)));
            throw new InvalidDataException("Unknown coop status payload format.");
        }

        private static Ei.ContractCoopStatusResponse DecodeLegacy(byte[] stored) {
            using var input = new MemoryStream(stored, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse>(Encoding.UTF8.GetString(output.ToArray()));
        }

        private static Ei.ContractCoopStatusResponse DecodeProto(byte[] stored) {
            using var input = new MemoryStream(stored, 1, stored.Length - 1, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            return WithRecomputedTimeLeft(Ei.ContractCoopStatusResponse.Parser.ParseFrom(gzip));
        }

        private static Ei.ContractCoopStatusResponse WithRecomputedTimeLeft(Ei.ContractCoopStatusResponse status) {
            foreach(var contributor in status.Contributors)
                contributor.TimeLeftSeconds = status.SecondsRemaining;
            return status;
        }
    }
}
