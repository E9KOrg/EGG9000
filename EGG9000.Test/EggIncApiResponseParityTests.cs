using Google.Protobuf;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class EggIncApiResponseParityTests {
        private static string FormatMessage(string body) {
            try {
                Convert.FromBase64String(body);
            } catch(FormatException e) {
                return e.Message;
            }
            throw new InvalidOperationException("body decoded");
        }

        private static byte[] FirstContactWithInvalidUtf8UserName() {
            var backup = new byte[] { 0x12, 0x02, 0xFF, 0xFE }.Concat(new Ei.Backup { Game = new Ei.Backup.Types.Game { SoulEggsD = 4242 } }.ToByteArray()).ToArray();
            return [0x0A, (byte)backup.Length, .. backup];
        }

        [TestMethod]
        public async Task Archive_NonSuccess_MasterHttpErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            var (value, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.IsNull(value);
            Assert.AreEqual("HTTP Error: NotFound", error);
        }

        [TestMethod]
        public async Task SeasonInfos_NonSuccess_MasterHttpErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.Forbidden));

            var (_, error) = await h.Api.GetSeasonInfosAsync();

            Assert.AreEqual("HTTP Error: Forbidden", error);
        }

        [TestMethod]
        public async Task Subscription_NonSuccess_MasterHttpErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.BadRequest));

            var (_, error) = await h.Api.GetUserSubscription(EggIncApiHarness.UserA);

            Assert.AreEqual("HTTP Error: BadRequest", error);
        }

        [TestMethod]
        public async Task ContractsInfo_NonSuccess_MasterHttpErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            var (value, error) = await h.Api.GetContractsInfoAsync(EggIncApiHarness.UserA, "golden-contract");

            Assert.IsNull(value);
            Assert.AreEqual("HTTP Error: NotFound", error);
        }

        [TestMethod]
        public async Task CallHelpers_TransportException_ReturnBotException() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Throw("socket closed");

            Assert.AreEqual("Bot Exception: socket closed", (await h.Api.GetContractsArchive(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual("Bot Exception: socket closed", (await h.Api.GetSeasonInfosAsync()).Error);
            Assert.AreEqual("Bot Exception: socket closed", (await h.Api.GetContractPlayerInfo(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual("Bot Exception: socket closed", (await h.Api.GetUserSubscription(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual("Bot Exception: socket closed", (await h.Api.GetContractsInfoAsync(EggIncApiHarness.UserA, "golden-contract")).Error);
        }

        [TestMethod]
        public async Task CallHelpers_NonBase64Body_ReturnBotExceptionWithFormatMessage() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Ok("!!not-base64!!");
            var expected = "Bot Exception: " + FormatMessage("!!not-base64!!");

            Assert.AreEqual(expected, (await h.Api.GetContractsArchive(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual(expected, (await h.Api.GetSeasonInfosAsync()).Error);
            Assert.AreEqual(expected, (await h.Api.GetContractPlayerInfo(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual(expected, (await h.Api.GetUserSubscription(EggIncApiHarness.UserA)).Error);
            Assert.AreEqual(expected, (await h.Api.GetContractsInfoAsync(EggIncApiHarness.UserA, "golden-contract")).Error);
        }

        [TestMethod]
        public async Task CallHelpers_BodyNotAuthenticatedMessage_ReturnBotException() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Ok(Convert.ToBase64String([0xFF, 0xFF, 0xFF]));

            var (value, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.IsNull(value);
            Assert.AreEqual("Bot Exception: Response is not an AuthenticatedMessage.", error);
        }

        [TestMethod]
        public async Task Archive_EmptyOk_SucceedsWithEmptyMessage() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(""));

            var (value, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.IsNull(error);
            Assert.IsNotNull(value);
            Assert.AreEqual(0, value.Archive.Count);
        }

        [TestMethod]
        public async Task SeasonInfos_ZlibCompressedEnvelope_Decodes() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.ContractSeasonInfos();
            reply.Infos.Add(new Ei.ContractSeasonInfo { Id = "fall_2026", Name = "Fall 2026" });
            h.Handler.Enqueue(Wire.AuthenticatedZlib(reply));

            var (value, error) = await h.Api.GetSeasonInfosAsync();

            Assert.IsNull(error);
            Assert.AreEqual("Fall 2026", value!.Infos[0].Name);
        }

        [TestMethod]
        public async Task SeasonInfos_NoSalt_SendsNothing() {
            using var h = new EggIncApiHarness(salt: false);

            var (value, error) = await h.Api.GetSeasonInfosAsync();

            Assert.IsNull(value);
            Assert.AreEqual("API salt not configured for ei_ctx/get_season_infos_v2", error);
            Assert.AreEqual(0, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task PlayerInfo_NoSalt_SendsNothing() {
            using var h = new EggIncApiHarness(salt: false);

            var (_, error) = await h.Api.GetContractPlayerInfo(EggIncApiHarness.UserA);

            Assert.AreEqual("API salt not configured for ei_ctx/get_contract_player_info", error);
            Assert.AreEqual(0, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task ContractsInfo_NoSalt_MasterErrorText() {
            using var h = new EggIncApiHarness(salt: false);

            var (_, error) = await h.Api.GetContractsInfoAsync(EggIncApiHarness.UserA, "golden-contract");

            Assert.AreEqual("API salt not configured for ei_ctx/get_contracts_info", error);
            Assert.AreEqual(0, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task PostSigned_NoSalt_ReturnsNullWithoutRequest() {
            using var h = new EggIncApiHarness(salt: false);

            var response = await h.Api.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(new Ei.ContractCoopStatusUpdateRequest { UserId = EggIncApiHarness.UserA }, EggIncApiHarness.UserA);

            Assert.IsNull(response);
            Assert.AreEqual(0, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task Post_NonSuccess_ReturnsNull() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            Assert.IsNull(await h.Api.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest(), EggIncApiHarness.UserA));
        }

        [TestMethod]
        public async Task Post_TransportException_ReturnsNull() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Throw("socket closed");

            Assert.IsNull(await h.Api.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(new Ei.JoinCoopRequest(), EggIncApiHarness.UserA));
        }

        [TestMethod]
        public async Task Send_NonSuccessAndException_ReturnFalse() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            Assert.IsFalse(await h.Api.Send(new Ei.KickPlayerCoopRequest(), EggIncApiHarness.FixedEid));

            h.Handler.Fallback = Wire.Throw("socket closed");
            Assert.IsFalse(await h.Api.Send(new Ei.KickPlayerCoopRequest(), EggIncApiHarness.FixedEid));
        }

        [TestMethod]
        public async Task BotFirstContact_NonSuccess_MasterErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            var response = await h.Api.BotFirstContact(EggIncApiHarness.UserA);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("Error response from API", response.Error);
        }

        [TestMethod]
        public async Task BotFirstContact_EmptyOk_MasterNoBackupText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(""));

            var response = await h.Api.BotFirstContact(EggIncApiHarness.UserA);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("bot_first_contact returned no backup", response.Error);
        }

        [TestMethod]
        public async Task BotFirstContact_TransportException_MasterBotExceptionText() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Throw("socket closed");

            var response = await h.Api.BotFirstContact(EggIncApiHarness.UserA);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("Bot Exception: socket closed", response.Error);
        }

        [TestMethod]
        public async Task BotFirstContact_InvalidUtf8UserName_RecoveredBySanitizer() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(Convert.ToBase64String(FirstContactWithInvalidUtf8UserName())));

            var response = await h.Api.BotFirstContact(EggIncApiHarness.UserA);

            Assert.IsTrue(response.Success);
            Assert.AreEqual("??", response.Backup.UserName);
            Assert.AreEqual(4242d, response.Backup.Game.SoulEggsD);
        }

        [TestMethod]
        public async Task FirstContact_NoSalt_ReturnsBotFailureWithoutFallback() {
            using var h = new EggIncApiHarness(salt: false);
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            var response = await h.Api.FirstContact(EggIncApiHarness.UserA);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("Error response from API", response.Error);
            Assert.AreEqual(1, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task FirstContact_SecureAlsoFails_MasterErrorText() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(""), Wire.Status(HttpStatusCode.NotFound));

            var response = await h.Api.FirstContact(EggIncApiHarness.UserA);

            Assert.IsFalse(response.Success);
            Assert.AreEqual("Error response from API", response.Error);
            Assert.AreEqual(2, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task GetBackup_Failure_PassesFirstContactError() {
            using var h = new EggIncApiHarness(salt: false);
            h.Handler.Enqueue(Wire.Ok(""));

            var (value, error) = await h.Api.GetBackupAsync(EggIncApiHarness.UserA, System.Collections.Frozen.FrozenSet<Ei.Contract>.Empty);

            Assert.IsNull(value);
            Assert.AreEqual("bot_first_contact returned no backup", error);
        }

        [TestMethod]
        public async Task CoopStatus_MismatchedCoop_ReturnsNull() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractCoopStatusResponse { CoopIdentifier = "other-coop" }));

            Assert.IsNull(await h.Api.GetCoopStatus("golden-contract", "golden-coop", EggIncApiHarness.UserA));
        }

        [TestMethod]
        public async Task CoopStatus_NonSuccess_ReturnsNull() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.NotFound));

            Assert.IsNull(await h.Api.GetCoopStatusBot("golden-contract", "golden-coop"));
        }

        [TestMethod]
        public async Task CoopStatus_TransportException_PropagatesLikeMaster() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Throw("socket closed");

            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => h.Api.GetCoopStatus("golden-contract", "golden-coop", EggIncApiHarness.UserA));
        }

        [TestMethod]
        public async Task CoopStatus_DepartedParticipant_RenamedFromXref() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.ContractCoopStatusResponse { CoopIdentifier = "golden-coop", SecondsRemaining = 60 };
            reply.Contributors.Add(new Ei.ContractCoopStatusResponse.Types.ContributionInfo { UserName = "[departed]", ContributionAmount = 1234 });
            h.Handler.Enqueue(Wire.Authenticated(reply));
            var xref = new Common.Database.Entities.UserCoopXref {
                LastStatus = new Common.Database.Entities.ContributionInfoCompact {
                    UserName = "Returned Farmer",
                    ContributionAmount = 1234
                }
            };

            var status = await h.Api.GetCoopStatus("golden-contract", "golden-coop", EggIncApiHarness.UserA, [xref]);

            Assert.AreEqual("Returned Farmer", status.Participants.Single().UserName);
        }
    }
}
