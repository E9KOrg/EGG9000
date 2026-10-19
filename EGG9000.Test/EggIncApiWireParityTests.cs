using Google.Protobuf;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class EggIncApiWireParityTests {
        private const string Contract = "golden-contract";

        private static async Task AssertPost(RecordedRequest request, string path, IReadOnlyDictionary<string, string> headers, Version version, byte[] expectedPayload) {
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(new Uri(Wire.Origin + path), request.Uri);
            Assert.AreEqual(version, request.Version);
            Wire.AssertHeaders(headers, request.Headers);
            Assert.AreEqual(Wire.FormContentType, request.ContentType);
            Assert.AreEqual(await Wire.MasterBody(expectedPayload), request.Body);
            CollectionAssert.AreEqual(expectedPayload, request.Payload);
        }

        private static Ei.ContractCoopStatusRequest MasterCoopStatusRequest(string eid, double timestamp) => new() {
            ContractIdentifier = Contract,
            CoopIdentifier = "mixedcase-coop",
            Rinfo = Wire.MasterInfo(eid),
            UserId = eid,
            ClientVersion = EggIncApiHarness.ClientVersion,
            ClientTimestamp = timestamp
        };

        private static Ei.EggIncFirstContactRequest MasterFirstContactRequest(string eid) => new() {
            ClientVersion = EggIncApiHarness.ClientVersion,
            Platform = Ei.Platform.Droid,
            EiUserId = eid,
            DeviceId = eid,
            Username = "",
            Rinfo = Wire.MasterInfo(eid)
        };

        private static Ei.GetPeriodicalsRequest MasterPeriodicalsRequest(uint clientVersion) => new() {
            UserId = EggIncApiHarness.PeriodicalsReferenceEid,
            PiggyFull = false,
            PiggyFoundFull = false,
            SecondsFullRealtime = 2339576.17448521,
            SecondsFullGametime = 391564.659540082,
            SoulEggs = 570149167.28294,
            CurrentClientVersion = clientVersion,
            Debug = false
        };

        private static Ei.PeriodicalsResponse PeriodicalsWithContract() {
            var response = new Ei.PeriodicalsResponse { Contracts = new Ei.ContractsResponse() };
            response.Contracts.Contracts.Add(new Ei.Contract { Identifier = Contract });
            return response;
        }

        [TestMethod]
        public async Task GetCoopStatus_MatchesMasterRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractCoopStatusResponse { CoopIdentifier = "mixedcase-coop" }));
            var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            var status = await h.Api.GetCoopStatus(Contract, "MixedCase-Coop", EggIncApiHarness.UserA);

            var request = h.Single();
            var timestamp = request.Message<Ei.ContractCoopStatusRequest>().ClientTimestamp;
            Assert.IsTrue(timestamp >= before && timestamp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            await AssertPost(request, "ei/coop_status", Wire.CoopStatusHeaders, Wire.Http20, MasterCoopStatusRequest(EggIncApiHarness.UserA, timestamp).ToByteArray());
            Assert.IsNotNull(status);
            Assert.IsTrue(status.Success);
        }

        [TestMethod]
        public async Task GetCoopStatus_NoEid_UsesFixedMasterEid() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractCoopStatusResponse { CoopIdentifier = "mixedcase-coop" }));

            await h.Api.GetCoopStatus(Contract, "mixedcase-coop");

            var request = h.Single();
            var timestamp = request.Message<Ei.ContractCoopStatusRequest>().ClientTimestamp;
            CollectionAssert.AreEqual(MasterCoopStatusRequest(EggIncApiHarness.FixedEid, timestamp).ToByteArray(), request.Payload);
        }

        [TestMethod]
        public async Task GetCoopStatusBot_MatchesMasterRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractCoopStatusResponse { CoopIdentifier = "mixedcase-coop" }));

            var status = await h.Api.GetCoopStatusBot(Contract, "MixedCase-Coop");

            var request = h.Single();
            var timestamp = request.Message<Ei.ContractCoopStatusRequest>().ClientTimestamp;
            await AssertPost(request, "ei/coop_status_bot", Wire.AndroidHeaders, Wire.Http11, MasterCoopStatusRequest(EggIncApiHarness.BotEid, timestamp).ToByteArray());
            Assert.IsTrue(status.Success);
        }

        [TestMethod]
        public async Task BotFirstContact_MatchesMasterRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.EggIncFirstContactResponse { Backup = new Ei.Backup { UserName = "Golden Farmer" } }));

            var response = await h.Api.BotFirstContact(EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei/bot_first_contact", Wire.AndroidHeaders, Wire.Http11, MasterFirstContactRequest(EggIncApiHarness.UserA).ToByteArray());
            Assert.IsTrue(response.Success);
            Assert.IsNull(response.Error);
            Assert.AreEqual("Golden Farmer", response.Backup.UserName);
        }

        [TestMethod]
        public async Task FirstContact_BotSucceeds_SendsOnlyBotRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.EggIncFirstContactResponse { Backup = new Ei.Backup { UserName = "Golden Farmer" } }));

            var response = await h.Api.FirstContact(EggIncApiHarness.UserA);

            Assert.AreEqual("ei/bot_first_contact", h.Single().Uri.AbsolutePath.TrimStart('/'));
            Assert.IsTrue(response.Success);
        }

        [TestMethod]
        public async Task FirstContact_BotEmpty_FallsBackToSignedSecureRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(""), Wire.Authenticated(new Ei.EggIncFirstContactResponse { Backup = new Ei.Backup { UserName = "Secure Farmer" } }));

            var response = await h.Api.FirstContact(EggIncApiHarness.UserA);

            Assert.AreEqual(2, h.Handler.Requests.Count);
            await AssertPost(h.Handler.Requests[0], "ei/bot_first_contact", Wire.AndroidHeaders, Wire.Http11, MasterFirstContactRequest(EggIncApiHarness.UserA).ToByteArray());
            await AssertPost(h.Handler.Requests[1], "ei/first_contact_secure", Wire.AndroidHeaders, Wire.Http11, Wire.MasterSigned(MasterFirstContactRequest(EggIncApiHarness.UserA).ToByteArray()));
            Assert.IsTrue(response.Success);
            Assert.AreEqual("Secure Farmer", response.Backup.UserName);
        }

        [TestMethod]
        public async Task FirstContact_NonEiId_SecureRequestCarriesLegacyUserIdOnly() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Ok(""), Wire.Plain(new Ei.EggIncFirstContactResponse { Backup = new Ei.Backup { UserName = "Legacy Farmer" } }));

            var response = await h.Api.FirstContact("legacy-user-0001");

            Assert.AreEqual(2, h.Handler.Requests.Count);
            var expected = new Ei.EggIncFirstContactRequest {
                ClientVersion = EggIncApiHarness.ClientVersion,
                Platform = Ei.Platform.Droid,
                UserId = "legacy-user-0001"
            };
            await AssertPost(h.Handler.Requests[1], "ei/first_contact_secure", Wire.AndroidHeaders, Wire.Http11, Wire.MasterSigned(expected.ToByteArray()));
            Assert.IsTrue(response.Success);
            Assert.AreEqual("Legacy Farmer", response.Backup.UserName);
        }

        [TestMethod]
        public async Task GetContractsInfo_MatchesMasterSignedRequest() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.ContractsInfoResponse();
            reply.NotFound.Add("missing-contract");
            h.Handler.Enqueue(Wire.Authenticated(reply));

            var (value, error) = await h.Api.GetContractsInfoAsync(EggIncApiHarness.UserA, Contract, "second-contract");

            var expected = new Ei.ContractsInfoRequest {
                ClientVersion = EggIncApiHarness.ClientVersion,
                Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA)
            };
            expected.ContractIdentifiers.Add(Contract);
            expected.ContractIdentifiers.Add("second-contract");
            await AssertPost(h.Single(), "ei_ctx/get_contracts_info", Wire.IosHeaders, Wire.Http11, Wire.MasterSigned(expected.ToByteArray()));
            Assert.IsNull(error);
            Assert.AreEqual("missing-contract", value!.NotFound[0]);
        }

        [TestMethod]
        public async Task GetContractsArchive_MatchesMasterRequest() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.ContractsArchive();
            reply.Archive.Add(new Ei.LocalContract { ContractIdentifier = Contract });
            h.Handler.Enqueue(Wire.Authenticated(reply));

            var (value, error) = await h.Api.GetContractsArchive(EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei_ctx/get_contracts_archive", Wire.AndroidHeaders, Wire.Http11, Wire.MasterInfo(EggIncApiHarness.UserA).ToByteArray());
            Assert.IsNull(error);
            Assert.AreEqual(Contract, value!.Archive[0].ContractIdentifier);
        }

        [TestMethod]
        public async Task GetSeasonInfos_MatchesMasterSignedRequest() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.ContractSeasonInfos();
            reply.Infos.Add(new Ei.ContractSeasonInfo { Id = "fall_2026" });
            h.Handler.Enqueue(Wire.Authenticated(reply));

            var (value, error) = await h.Api.GetSeasonInfosAsync();

            await AssertPost(h.Single(), "ei_ctx/get_season_infos_v2", Wire.AndroidHeaders, Wire.Http11, Wire.MasterSigned(Wire.MasterInfo(EggIncApiHarness.FixedEid).ToByteArray()));
            Assert.IsNull(error);
            Assert.AreEqual("fall_2026", value!.Infos[0].Id);
        }

        [TestMethod]
        public async Task GetContractPlayerInfo_MatchesMasterSignedRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractPlayerInfo { Grade = Ei.Contract.Types.PlayerGrade.GradeAaa }));

            var (value, error) = await h.Api.GetContractPlayerInfo(EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei_ctx/get_contract_player_info", Wire.AndroidHeaders, Wire.Http11, Wire.MasterSigned(Wire.MasterInfo(EggIncApiHarness.UserA).ToByteArray()));
            Assert.IsNull(error);
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAaa, value!.Grade);
        }

        [TestMethod]
        public async Task GetUserSubscription_MatchesMasterBodylessRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.UserSubscriptionInfo { SubscriptionLevel = Ei.UserSubscriptionInfo.Types.Level.Pro }));

            var (value, error) = await h.Api.GetUserSubscription(EggIncApiHarness.UserA);

            var request = h.Single();
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual(new Uri(Wire.Origin + "ei_srv/subscription_status/" + EggIncApiHarness.UserA), request.Uri);
            Assert.AreEqual(Wire.Http11, request.Version);
            Wire.AssertHeaders(Wire.IosHeaders, request.Headers);
            Assert.IsNull(request.ContentType);
            Assert.IsNull(request.Body);
            Assert.IsNull(error);
            Assert.AreEqual(Ei.UserSubscriptionInfo.Types.Level.Pro, value!.SubscriptionLevel);
        }

        [TestMethod]
        public async Task GetPeriodicals_MatchesMasterRequestWithoutRinfoUser() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(PeriodicalsWithContract()));

            var response = await h.Api.GetPeriodicalsAsync();

            var expected = MasterPeriodicalsRequest(EggIncApiHarness.ClientVersion);
            expected.Rinfo = Wire.MasterInfo(null);
            await AssertPost(h.Single(), "ei/get_periodicals", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.AreEqual(Contract, response.Contracts.Contracts[0].Identifier);
        }

        [TestMethod]
        public async Task ValidateVersions_SendsCandidateTriple_WithoutMutatingGlobals() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(PeriodicalsWithContract()));

            var valid = await h.Api.ValidateVersionsAsync(80, "1.40.0", "1.40.0.2");

            var expected = MasterPeriodicalsRequest(80);
            expected.Rinfo = Wire.MasterInfo(null, 80, "1.40.0", "1.40.0.2");
            await AssertPost(h.Single(), "ei/get_periodicals", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.IsTrue(valid);
            Assert.AreEqual(EggIncApiHarness.ClientVersion, Common.EggIncAPI.EggIncApi.ClientVersion);
            Assert.AreEqual(EggIncApiHarness.AppVersion, Common.EggIncAPI.EggIncApi.AppVersion);
            Assert.AreEqual(EggIncApiHarness.AppBuild, Common.EggIncAPI.EggIncApi.AppBuild);
        }

        [TestMethod]
        public async Task SendKickPlayer_LeavesCallerRinfo_OnAndroidProfile() {
            using var h = new EggIncApiHarness();
            var kick = new Ei.KickPlayerCoopRequest {
                Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private,
                ClientVersion = EggIncApiHarness.ClientVersion,
                ContractIdentifier = Contract,
                CoopIdentifier = "golden-coop",
                PlayerIdentifier = EggIncApiHarness.FixedEid,
                RequestingUserId = EggIncApiHarness.FixedEid,
                Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA)
            };
            var expected = kick.ToByteArray();

            var sent = await h.Api.Send(kick, EggIncApiHarness.FixedEid);

            await AssertPost(h.Single(), "ei/kick_player_coop", Wire.AndroidHeaders, Wire.Http11, expected);
            Assert.IsTrue(sent);
        }

        [TestMethod]
        public async Task PostJoinCoop_InjectsRinfoForUser_PlainResponse() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.JoinCoopResponse { Success = true, CoopIdentifier = "golden-coop" }));
            var join = new Ei.JoinCoopRequest {
                ContractIdentifier = Contract,
                CoopIdentifier = "golden-coop",
                UserId = EggIncApiHarness.UserA,
                ClientVersion = EggIncApiHarness.ClientVersion,
                Platform = Ei.Platform.Droid,
                UserName = "."
            };
            var expected = join.Clone();
            expected.Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA);

            var response = await h.Api.Post<Ei.JoinCoopResponse, Ei.JoinCoopRequest>(join, EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei/join_coop", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.IsTrue(response.Success);
        }

        [TestMethod]
        public async Task PostUpdateCoopPermissions_PresetRinfoForOtherUser_ReplacedLikeMaster() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.UpdateCoopPermissionsResponse { Success = true }));
            var update = new Ei.UpdateCoopPermissionsRequest {
                ClientVersion = EggIncApiHarness.ClientVersion,
                ContractIdentifier = Contract,
                CoopIdentifier = "golden-coop",
                Public = false,
                RequestingUserId = EggIncApiHarness.UserA,
                Rinfo = Wire.MasterInfo("EI2222222222222222", 1, "0.0.1", "0.0.1.1")
            };
            var expected = update.Clone();
            expected.Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA);

            var response = await h.Api.Post<Ei.UpdateCoopPermissionsResponse, Ei.UpdateCoopPermissionsRequest>(update, EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei/update_coop_permissions", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.IsTrue(response.Success);
        }

        [TestMethod]
        public async Task PostCreateCoop_MatchesMasterRequest() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.CreateCoopResponse { Success = true }));
            var create = new Ei.CreateCoopRequest {
                ContractIdentifier = Contract,
                CoopIdentifier = "golden-coop",
                SecondsRemaining = 86400,
                UserId = EggIncApiHarness.UserA,
                UserName = "Golden Farmer",
                ClientVersion = EggIncApiHarness.ClientVersion
            };
            var expected = create.Clone();
            expected.Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA);

            var response = await h.Api.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(create, EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei/create_coop", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.IsTrue(response.Success);
        }

        [TestMethod]
        public async Task PostCoopStatusUpdate_SignedSecureEndpoint_PlainResponse() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Plain(new Ei.ContractCoopStatusUpdateResponse { Exists = true }));
            var update = new Ei.ContractCoopStatusUpdateRequest {
                UserId = EggIncApiHarness.UserA,
                ContractIdentifier = Contract,
                CoopIdentifier = "golden-coop",
                Amount = 0,
                EggLayingRateBuff = 1,
                EarningsBuff = 1
            };
            var expected = update.Clone();
            expected.Rinfo = Wire.MasterInfo(EggIncApiHarness.UserA);

            var response = await h.Api.Post<Ei.ContractCoopStatusUpdateResponse, Ei.ContractCoopStatusUpdateRequest>(update, EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei/update_coop_status_secure", Wire.IosHeaders, Wire.Http11, Wire.MasterSigned(expected.ToByteArray()));
            Assert.IsTrue(response.Exists);
        }

        [TestMethod]
        public async Task PostConfig_AuthenticatedOverride_UnwrapsResponse() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ConfigResponse { LiveConfig = new Ei.LiveConfig { ConfigId = "cfg-1" } }));
            var config = new Ei.ConfigRequest {
                ArtifactsUnlocked = true,
                FuelTankUnlocked = true,
                SoulEggs = 2e30
            };
            var expected = config.Clone();
            expected.Rinfo = Wire.MasterInfo(EggIncApiHarness.FixedEid);

            var response = await h.Api.Post<Ei.ConfigResponse, Ei.ConfigRequest>(config, EggIncApiHarness.FixedEid, true);

            await AssertPost(h.Single(), "ei/get_config", Wire.IosHeaders, Wire.Http11, expected.ToByteArray());
            Assert.AreEqual("cfg-1", response.LiveConfig.ConfigId);
        }

        [TestMethod]
        public async Task PostBasicRequestInfo_PlayerInfo_UsesIosSignedRoute() {
            using var h = new EggIncApiHarness();
            h.Handler.Enqueue(Wire.Authenticated(new Ei.ContractPlayerInfo { Grade = Ei.Contract.Types.PlayerGrade.GradeAa }));

            var response = await h.Api.Post<Ei.ContractPlayerInfo, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei_ctx/get_contract_player_info", Wire.IosHeaders, Wire.Http11, Wire.MasterSigned(Wire.MasterInfo(EggIncApiHarness.UserA).ToByteArray()));
            Assert.AreEqual(Ei.Contract.Types.PlayerGrade.GradeAa, response.Grade);
        }

        [TestMethod]
        public async Task PostBasicRequestInfo_MyContracts_UsesIosArchiveRoute() {
            using var h = new EggIncApiHarness();
            var reply = new Ei.MyContracts();
            reply.Archive.Add(new Ei.LocalContract { ContractIdentifier = Contract });
            h.Handler.Enqueue(Wire.Authenticated(reply));

            var response = await h.Api.Post<Ei.MyContracts, Ei.BasicRequestInfo>(new Ei.BasicRequestInfo(), EggIncApiHarness.UserA);

            await AssertPost(h.Single(), "ei_ctx/get_contracts_archive", Wire.IosHeaders, Wire.Http11, Wire.MasterInfo(EggIncApiHarness.UserA).ToByteArray());
            Assert.AreEqual(Contract, response.Archive[0].ContractIdentifier);
        }

        [TestMethod]
        public void Signer_MatchesMasterHashAlgorithm() {
            var shortMessage = new byte[] { 0x0A, 0x03, 0x61, 0x62, 0x63 };
            var longMessage = new byte[40];
            for(var i = 0; i < longMessage.Length; i++)
                longMessage[i] = (byte)i;
            var signer = new AuxbrainSigner(EggIncApiHarness.Salt);

            Assert.AreEqual("4956ddba5bf2b866ff2d2e0efd22c2fbe2bb5c87e8b4759cd4933a3e791cff9d", signer.Sign(shortMessage));
            Assert.AreEqual("01e3e3f856088ac82018dc4580e2cbdde08ce2b9e2ac90bd73e10c4956972a4c", signer.Sign(longMessage));
            Assert.AreEqual(Wire.MasterHash(shortMessage, EggIncApiHarness.Salt), signer.Sign(shortMessage));
            Assert.AreEqual(Wire.MasterHash(longMessage, EggIncApiHarness.Salt), signer.Sign(longMessage));
            CollectionAssert.AreEqual(new byte[] { 0x0A, 0x03, 0x61, 0x62, 0x63 }, shortMessage);
        }

        [TestMethod]
        public void HttpClients_MatchMasterTimeoutAndVersion() {
            using var h = new EggIncApiHarness();
            var factory = h.Services.GetRequiredService<IHttpClientFactory>();

            foreach(var (profile, version) in new[] { ("Android", Wire.Http11), ("Ios", Wire.Http11), ("CoopStatus", Wire.Http20) }) {
                var client = factory.CreateClient("EggSharp.Api." + profile);
                Assert.AreEqual(TimeSpan.FromSeconds(30), client.Timeout, profile);
                Assert.AreEqual(version, client.DefaultRequestVersion, profile);
            }
        }
    }
}
