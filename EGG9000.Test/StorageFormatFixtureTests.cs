using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class StorageFormatFixtureTests {
        private const byte AccountsDictionaryId = 242;
        private const byte CoopDictionaryId = 243;

        private const string AccountsDictionaryText = "Golden Farmer EI-synthetic-0042 Second Farmer EI-synthetic-0043 Third Farmer EI-synthetic-0044 Golden Farmer EI-synthetic-0042 Second Farmer EI-synthetic-0043 Third Farmer EI-synthetic-0044 ";
        private const string CoopDictionaryText = "golden-contract golden-coop EI-synthetic-0042 Golden Farmer golden-contract golden-coop EI-synthetic-0042 Golden Farmer ";

        private const string CoopProto = "0A0F676F6C64656E2D636F6E74726163741100000000D01243411A0B676F6C64656E2D636F6F70222B0A1145492D73796E7468657469632D30303432120D476F6C64656E204661726D65721900000000D012334129000000000020BC40";
        private const string CoopEnvelopeRaw = "EB000A0F676F6C64656E2D636F6E74726163741100000000D01243411A0B676F6C64656E2D636F6F70222B0A1145492D73796E7468657469632D30303432120D476F6C64656E204661726D65721900000000D012334129000000000020BC40";
        private const string CoopEnvelopeGzip = "EB011F8B08000000000002FFE3E24FCFCF4949CDD34DCECF2B294A4C2E116400820B42CE8E52DC7099FC02256D2E41574FDDE2CABC928CD492CC645D03031323215E77B00A05B7C4A2DCD42249884E63474D108341618F0300195160925D000000";
        private const string CoopEnvelopeBrotli = "EB02C005100A0F676F6C64656E2D636F6E74726163741100000000D01243411A0B676F6C64656E2D636F6F70222B0A1145492D73796E7468657469632D30303432120D476F6C64656E204661726D65721900000000D012334129000000000020BC4003";
        private const string CoopEnvelopeZstd = "EB030028B52FFD205DB5020094040A0F676F6C64656E2D636F6E74726163741100000000D01243411A0B6F70222B0A1145492D73796E7468657469632D30303432120D47204661726D657219334129000000000020BC4003005D0DB7A83BE862E601";
        private const string CoopEnvelopeZstdDictionary = "EB03F328B52FFD205D850100F80A0F1100000000D01243411A0B222B0A11120D19334129000000000020BC4005005D07F1C4819D986489D8075DC002";
        private const string CoopLegacyProtoMarker = "E91F8B08000000000002FFE3E24FCFCF4949CDD34DCECF2B294A4C2E116400820B42CE8E52DC7099FC02256D2E41574FDDE2CABC928CD492CC645D03031323215E77B00A05B7C4A2DCD42249884E63474D108341618F0300195160925D000000";
        private const string CoopLegacyJson = "1F8B08000000000002FF6D8E3D0BC2301086FF8ADC9C965814219B884A17073F267188EDD9069A5C49AE8314FFBB69ABE0E06DC7F3BE0F6F0F1B72EC75C179898ECDC3A007051535F14D8A0F03016762DDAC2D758E41654B395C2A45AC53FBBF4A2D88C96EEE1D930FA0AE3D5C02FABC8CB96D9E84A7E31AD91489948B2CA60778D01623DE8F9AD94E7B1BB53F2243EE3B633EED48E5EB26E084716D198E68B571C655A056D9C8DEE8DF0FE1E3000000";

        private const string AccountsMsgpack = "9392AD476F6C64656E204661726D6572B145492D73796E7468657469632D3030343292AD5365636F6E64204661726D6572B145492D73796E7468657469632D3030343392AC5468697264204661726D6572B145492D73796E7468657469632D30303434";
        private const string AccountsLegacyGzip = "1F8B08000000000002FF9B3C69AD7B7E4E4A6A9E825B62516E6AD146574FDDE2CABC928CD492CC645D030313A3496B835393F3F3527029309EB4262423B308A7BC09000FFA296163000000";
        private const string AccountsEnvelopeRaw = "EB009392AD476F6C64656E204661726D6572B145492D73796E7468657469632D3030343292AD5365636F6E64204661726D6572B145492D73796E7468657469632D3030343392AC5468697264204661726D6572B145492D73796E7468657469632D30303434";
        private const string AccountsEnvelopeGzip = "EB011F8B08000000000002FF9B3C69AD7B7E4E4A6A9E825B62516E6AD146574FDDE2CABC928CD492CC645D030313A3496B835393F3F3527029309EB4262423B308A7BC09000FFA296163000000";
        private const string AccountsEnvelopeBrotli = "EB022006109392AD476F6C64656E204661726D6572B145492D73796E7468657469632D3030343292AD5365636F6E64204661726D6572B145492D73796E7468657469632D3030343392AC5468697264204661726D6572B145492D73796E7468657469632D3030343403";
        private const string AccountsEnvelopeZstd = "EB030028B52FFD2063E5010024039392AD476F6C64656E204661726D6572B145492D73796E7468657469632D3030343292AD5365636F6E643392AC54686972340200432D46045465";
        private const string AccountsEnvelopeZstdDictionary = "EB03F228B52FFD2063DD0000509392ADB192ADB192ACB10600308B3370083333C3D16016E51186";

        private static readonly StorageDictionary AccountsDictionary = StorageDictionary.Register(AccountsDictionaryId, "fixture-accounts", Encoding.ASCII.GetBytes(AccountsDictionaryText));
        private static readonly StorageDictionary CoopDictionary = StorageDictionary.Register(CoopDictionaryId, "fixture-coopstatus", Encoding.ASCII.GetBytes(CoopDictionaryText));

        private static byte[] Hex(string hex) => Convert.FromHexString(hex);

        private static void AssertCoop(Ei.ContractCoopStatusResponse status) {
            Assert.IsNotNull(status);
            Assert.AreEqual("golden-contract", status.ContractIdentifier);
            Assert.AreEqual("golden-coop", status.CoopIdentifier);
            Assert.AreEqual(2_500_000d, status.TotalAmount);
            Assert.AreEqual(7200d, status.SecondsRemaining);
            Assert.AreEqual(1, status.Contributors.Count);
            Assert.AreEqual("EI-synthetic-0042", status.Contributors[0].UserId);
            Assert.AreEqual("Golden Farmer", status.Contributors[0].UserName);
            Assert.AreEqual(1_250_000d, status.Contributors[0].ContributionAmount);
        }

        private static void AssertAccounts(List<EggIncAccount> accounts) {
            Assert.AreEqual(3, accounts.Count);
            Assert.AreEqual("Golden Farmer", accounts[0].Name);
            Assert.AreEqual("EI-synthetic-0042", accounts[0].Id);
            Assert.AreEqual("Second Farmer", accounts[1].Name);
            Assert.AreEqual("EI-synthetic-0043", accounts[1].Id);
            Assert.AreEqual("Third Farmer", accounts[2].Name);
            Assert.AreEqual("EI-synthetic-0044", accounts[2].Id);
        }

        [TestMethod]
        public void Envelope_EveryAlgorithm_DecodesIndependentlyEncodedBytes() {
            Assert.IsNotNull(CoopDictionary);
            var plain = Hex(CoopProto);

            foreach(var stored in new[] { CoopEnvelopeRaw, CoopEnvelopeGzip, CoopEnvelopeBrotli, CoopEnvelopeZstd, CoopEnvelopeZstdDictionary })
                CollectionAssert.AreEqual(plain, StorageCompression.Decompress(Hex(stored)), stored[..6]);
        }

        [TestMethod]
        public void CoopStatus_EveryHistoricalFormat_DecodesIdentically() {
            Assert.IsNotNull(CoopDictionary);
            AssertCoop(Ei.ContractCoopStatusResponse.Parser.ParseFrom(Hex(CoopProto)));

            foreach(var stored in new[] { CoopLegacyJson, CoopLegacyProtoMarker, CoopEnvelopeRaw, CoopEnvelopeGzip, CoopEnvelopeBrotli, CoopEnvelopeZstd, CoopEnvelopeZstdDictionary })
                AssertCoop(CoopStatusCodec.Decode(Hex(stored)));
        }

        [TestMethod]
        public void CoopStatus_EnvelopeAndProtoMarker_RecomputeTimeLeft() {
            Assert.IsNotNull(CoopDictionary);
            foreach(var stored in new[] { CoopLegacyProtoMarker, CoopEnvelopeZstdDictionary })
                Assert.AreEqual(7200d, CoopStatusCodec.Decode(Hex(stored)).Contributors[0].TimeLeftSeconds);
        }

        [TestMethod]
        public void Accounts_EveryHistoricalFormat_DecodesIdentically() {
            Assert.IsNotNull(AccountsDictionary);
            foreach(var stored in new[] { AccountsMsgpack, AccountsLegacyGzip, AccountsEnvelopeRaw, AccountsEnvelopeGzip, AccountsEnvelopeBrotli, AccountsEnvelopeZstd, AccountsEnvelopeZstdDictionary })
                AssertAccounts(StorageCodec.Unpack<List<EggIncAccount>>(Hex(stored)));
        }

        [TestMethod]
        public void Compress_HeaderLayout_MatchesMasterEnvelope() {
            var plain = Hex(CoopProto);

            var raw = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Raw));
            CollectionAssert.AreEqual(Hex(CoopEnvelopeRaw), raw);

            var zstd = StorageCompression.Compress(plain, new StorageCompressionStrategy(StorageCompressionAlgorithm.Zstd, rawThreshold: 0, dictionary: CoopDictionary));
            Assert.AreEqual(StorageCompression.Marker, zstd[0]);
            Assert.AreEqual((byte)StorageCompressionAlgorithm.Zstd, zstd[1]);
            Assert.AreEqual(CoopDictionaryId, zstd[2]);
            CollectionAssert.AreEqual(new byte[] { 0x28, 0xB5, 0x2F, 0xFD }, zstd[3..7]);
            CollectionAssert.AreEqual(plain, StorageCompression.Decompress(zstd));
        }

        [TestMethod]
        public void EnvelopeByteValues_MatchMaster() {
            byte[] actual = [
                StorageCompression.Marker,
                StorageDictionary.None,
                (byte)StorageCompressionAlgorithm.Raw,
                (byte)StorageCompressionAlgorithm.GZip,
                (byte)StorageCompressionAlgorithm.Brotli,
                (byte)StorageCompressionAlgorithm.Zstd,
            ];
            CollectionAssert.AreEqual(new byte[] { 0xEB, 0, 0, 1, 2, 3 }, actual);
        }
    }
}
