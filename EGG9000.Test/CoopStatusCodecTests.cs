using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class CoopStatusCodecTests {

        private static Ei.ContractCoopStatusResponse SampleStatus() {
            var status = new Ei.ContractCoopStatusResponse {
                ContractIdentifier = "test-contract",
                CoopIdentifier = "test-coop",
                TotalAmount = 1_000_000,
                SecondsRemaining = 3600,
                ClearedForExit = false
            };
            status.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo {
                UserId = "EI123",
                UserName = "Tester",
                ContributionAmount = 500_000,
                ContributionRate = 100,
                SoulPower = 20
            });
            return status;
        }

        private static T WithFlag<T>(bool enabled, Func<T> action) {
            var previous = CoopStatusCodec.ProtoWriteEnabled;
            CoopStatusCodec.ProtoWriteEnabled = enabled;
            try {
                return action();
            } finally {
                CoopStatusCodec.ProtoWriteEnabled = previous;
            }
        }

        [TestMethod]
        public void ProtoRoundTrip_PreservesFields() {
            var encoded = WithFlag(true, () => CoopStatusCodec.Encode(SampleStatus()));
            var decoded = CoopStatusCodec.Decode(encoded);

            Assert.AreEqual("test-contract", decoded.ContractIdentifier);
            Assert.AreEqual("test-coop", decoded.CoopIdentifier);
            Assert.AreEqual(1_000_000d, decoded.TotalAmount);
            Assert.AreEqual(1, decoded.Contributors.Count);
            Assert.AreEqual("Tester", decoded.Contributors[0].UserName);
            Assert.AreEqual(500_000d, decoded.Contributors[0].ContributionAmount);
        }

        [TestMethod]
        public void LegacyRoundTrip_PreservesFields() {
            var encoded = WithFlag(false, () => CoopStatusCodec.Encode(SampleStatus()));
            var decoded = CoopStatusCodec.Decode(encoded);

            Assert.AreEqual("test-contract", decoded.ContractIdentifier);
            Assert.AreEqual("test-coop", decoded.CoopIdentifier);
            Assert.AreEqual(1_000_000d, decoded.TotalAmount);
            Assert.AreEqual(1, decoded.Contributors.Count);
            Assert.AreEqual("Tester", decoded.Contributors[0].UserName);
            Assert.AreEqual(500_000d, decoded.Contributors[0].ContributionAmount);
        }

        [TestMethod]
        public void LegacyEncode_IsByteIdenticalToOldSetterAlgorithm() {
            var status = SampleStatus();
            var encoded = WithFlag(false, () => CoopStatusCodec.Encode(status));

            var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(status, new JsonSerializerSettings { ContractResolver = new CustomContractResolver() }));
            byte[] expected;
            using(var msi = new MemoryStream(bytes))
            using(var mso = new MemoryStream()) {
                using(var gs = new GZipStream(mso, CompressionMode.Compress)) {
                    var buffer = new byte[4096];
                    int cnt;
                    while((cnt = msi.Read(buffer, 0, buffer.Length)) != 0) {
                        gs.Write(buffer, 0, cnt);
                    }
                }
                expected = mso.ToArray();
            }

            CollectionAssert.AreEqual(expected, encoded);
        }

        [TestMethod]
        public void Encode_FirstBytes_DiscriminateFormats() {
            var legacy = WithFlag(false, () => CoopStatusCodec.Encode(SampleStatus()));
            var proto = WithFlag(true, () => CoopStatusCodec.Encode(SampleStatus()));

            Assert.AreEqual(0x1F, legacy[0]);
            Assert.AreEqual(0x8B, legacy[1]);
            Assert.AreEqual(StorageCompression.Marker, proto[0]);
        }

        [TestMethod]
        public void Decode_LegacyProtoMarkerBytes_StillReads() {
            var plain = Google.Protobuf.MessageExtensions.ToByteArray(SampleStatus());
            byte[] stored;
            using(var output = new MemoryStream()) {
                output.WriteByte(0xE9);
                using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
                    gzip.Write(plain, 0, plain.Length);
                stored = output.ToArray();
            }

            var decoded = CoopStatusCodec.Decode(stored);

            Assert.AreEqual("test-coop", decoded.CoopIdentifier);
            Assert.AreEqual(3600d, decoded.Contributors[0].TimeLeftSeconds);
        }

        [TestMethod]
        public void ProtoDecode_RecomputesTimeLeftSeconds() {
            var encoded = WithFlag(true, () => CoopStatusCodec.Encode(SampleStatus()));
            var decoded = CoopStatusCodec.Decode(encoded);

            Assert.AreEqual(3600d, decoded.Contributors[0].TimeLeftSeconds);
        }

        [TestMethod]
        public void Decode_Null_ReturnsNull() {
            Assert.IsNull(CoopStatusCodec.Decode(null));
        }

        [TestMethod]
        public void Decode_UnknownFormat_Throws() {
            Assert.ThrowsExactly<InvalidDataException>(() => CoopStatusCodec.Decode(new byte[] { 0x00, 0x01, 0x02 }));
        }

        [TestMethod]
        public void Encode_IsDeterministic() {
            var protoFirst = WithFlag(true, () => CoopStatusCodec.Encode(SampleStatus()));
            var protoSecond = WithFlag(true, () => CoopStatusCodec.Encode(SampleStatus()));
            CollectionAssert.AreEqual(protoFirst, protoSecond);

            var legacyFirst = WithFlag(false, () => CoopStatusCodec.Encode(SampleStatus()));
            var legacySecond = WithFlag(false, () => CoopStatusCodec.Encode(SampleStatus()));
            CollectionAssert.AreEqual(legacyFirst, legacySecond);
        }

        [TestMethod]
        public void CoopProperty_RoundTripsThroughCodec() {
            var stored = WithFlag(true, () => {
                var coop = new Coop { LastStatusUpdate = SampleStatus() };
                return coop._StatusCompressed;
            });
            var reloaded = new Coop { _StatusCompressed = stored };

            Assert.AreEqual("test-coop", reloaded.LastStatusUpdate.CoopIdentifier);
        }

        [TestMethod]
        public void CoopProperty_SuppressesUnchangedWrites() {
            WithFlag(true, () => {
                var coop = new Coop { LastStatusUpdate = SampleStatus() };
                var blob = coop._StatusCompressed;
                coop.LastStatusUpdate = SampleStatus();
                Assert.AreSame(blob, coop._StatusCompressed);
                return true;
            });
        }
    }
}
