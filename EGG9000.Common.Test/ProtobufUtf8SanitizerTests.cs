using EGG9000.Common.EggIncAPI;

using Google.Protobuf;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class ProtobufUtf8SanitizerTests {

        // A backup whose user_name carries invalid UTF-8 bytes used to throw "String is invalid UTF-8."
        // and lose the entire backup. The sanitizer must recover it, scrubbing only the bad bytes.
        [TestMethod]
        public void Recovers_backup_with_invalid_utf8_username() {
            var corrupt = BuildBackupWithRawUserName([0xFF, 0xFE], soulEggs: 12345.0);

            // Sanity: the raw bytes really do break the strict parser.
            Assert.ThrowsExactly<InvalidProtocolBufferException>(() => Ei.Backup.Parser.ParseFrom(corrupt));

            var recovered = EggIncApi.ParseTolerant<Ei.Backup>(corrupt);

            Assert.IsNotNull(recovered);
            Assert.AreEqual(12345.0, recovered.Game.SoulEggsD, "fields after the bad string must survive");
            Assert.AreEqual("??", recovered.UserName, "each invalid byte becomes a single '?'");
        }

        [TestMethod]
        public void Leaves_valid_utf8_untouched() {
            var clean = new Ei.Backup { UserName = "café 中文", Game = new Ei.Backup.Types.Game { SoulEggsD = 7 } };
            var bytes = clean.ToByteArray();

            var sanitized = ProtobufUtf8Sanitizer.Sanitize(bytes);

            CollectionAssert.AreEqual(bytes, sanitized, "valid input must round-trip byte-identical");
        }

        [TestMethod]
        public void Recurses_into_nested_message_strings() {
            // Farms is a repeated nested message; prove nested traversal doesn't damage a clean message.
            var backup = new Ei.Backup { UserName = "ok", Game = new Ei.Backup.Types.Game { SoulEggsD = 1 } };
            backup.Farms.Add(new Ei.Backup.Types.Simulation());
            var bytes = backup.ToByteArray();

            // No corruption here - just prove nested traversal doesn't damage a clean nested message.
            var sanitized = ProtobufUtf8Sanitizer.Sanitize(bytes);
            var reparsed = Ei.Backup.Parser.ParseFrom(sanitized);
            Assert.AreEqual(1, reparsed.Farms.Count);
            Assert.AreEqual("ok", reparsed.UserName);
        }

        // Builds a Backup wire payload by hand so we can inject raw (invalid) bytes into the user_name
        // string field (field 2, wire type 2), which the generated builder would otherwise reject.
        private static byte[] BuildBackupWithRawUserName(byte[] rawUserName, double soulEggs) {
            // tag = (field 2 << 3) | wire type 2 = 0x12, then a single-byte length (payloads are tiny).
            var head = new System.Collections.Generic.List<byte> { 0x12, (byte)rawUserName.Length };
            head.AddRange(rawUserName);

            // Append a clean Game submessage so we can assert fields after the bad string survive.
            var tail = new Ei.Backup { Game = new Ei.Backup.Types.Game { SoulEggsD = soulEggs } }.ToByteArray();
            head.AddRange(tail);
            return head.ToArray();
        }
    }
}
