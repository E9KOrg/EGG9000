using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Net;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class EggIncApiRetryTests {
        private static Ei.ContractsArchive Archive() {
            var archive = new Ei.ContractsArchive();
            archive.Archive.Add(new Ei.LocalContract { ContractIdentifier = "golden-contract" });
            return archive;
        }

        [TestMethod]
        public async Task ServerError_RetriedTwice_ThenSucceeds() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.InternalServerError), Wire.Status(HttpStatusCode.BadGateway), Wire.Authenticated(Archive()));

            var (value, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.AreEqual(3, h.Handler.Requests.Count);
            Assert.IsNull(error);
            Assert.AreEqual("golden-contract", value!.Archive[0].ContractIdentifier);
        }

        [TestMethod]
        public async Task ServerError_GivesUpAfterThreeAttempts() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Status(HttpStatusCode.ServiceUnavailable);

            var (_, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.AreEqual(3, h.Handler.Requests.Count);
            Assert.AreEqual("HTTP Error: ServiceUnavailable", error);
        }

        [TestMethod]
        public async Task TooManyRequests_IsRetried() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.TooManyRequests), Wire.Authenticated(Archive()));

            var (_, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.AreEqual(2, h.Handler.Requests.Count);
            Assert.IsNull(error);
        }

        [TestMethod]
        public async Task TransportException_RetriedTwice_ThenBotException() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Throw("socket closed");

            var (_, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.AreEqual(3, h.Handler.Requests.Count);
            Assert.AreEqual("Bot Exception: socket closed", error);
        }

        [TestMethod]
        public async Task ClientError_IsNotRetried() {
            using var h = new EggIncApiHarness();
            h.Handler.Fallback = Wire.Status(HttpStatusCode.NotFound);

            await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            Assert.AreEqual(1, h.Handler.Requests.Count);
        }

        [TestMethod]
        public async Task RetriedAttempts_ResendIdenticalBody() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.InternalServerError), Wire.Authenticated(new Ei.ContractPlayerInfo()));

            await h.Api.GetContractPlayerInfo(EggIncApiHarness.UserA);

            Assert.AreEqual(2, h.Handler.Requests.Count);
            Assert.IsNotNull(h.Handler.Requests[0].Body);
            Assert.AreEqual(h.Handler.Requests[0].Body, h.Handler.Requests[1].Body);
        }

        [TestMethod]
        public async Task Send_ServerError_IsRetried() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Status(HttpStatusCode.InternalServerError), Wire.Ok(""));

            var sent = await h.Api.Send(new Ei.KickPlayerCoopRequest(), EggIncApiHarness.FixedEid);

            Assert.AreEqual(2, h.Handler.Requests.Count);
            Assert.IsTrue(sent);
        }
    }
}
