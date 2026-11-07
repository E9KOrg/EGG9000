using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public sealed partial class EggIncApi {

        private static GetPeriodicalsRequest BuildPeriodicalsRequest(string userId, uint clientVersion) => new() {
            UserId = userId,
            PiggyFull = false,
            PiggyFoundFull = false,
            SecondsFullRealtime = 2339576.17448521,
            SecondsFullGametime = 391564.659540082,
            SoulEggs = 570149167.28294,
            CurrentClientVersion = clientVersion,
            Debug = false,
        };

        public Task<PeriodicalsResponse> GetPeriodicalsAsync(string userId = PeriodicalsReferenceUserId) =>
            Post<PeriodicalsResponse, GetPeriodicalsRequest>(BuildPeriodicalsRequest(userId, ClientVersion), PeriodicalsPostUserId, true);

        public static bool IsValidPeriodicalsResponse(PeriodicalsResponse resp) => (resp?.Contracts?.Contracts?.Count ?? 0) > 0;

        public async Task<bool> ValidateVersionsAsync(uint clientVersion, string appVersion, string appBuild, string userId = PeriodicalsReferenceUserId) {
            try {
                var request = BuildPeriodicalsRequest(userId, clientVersion);
                request.Rinfo = new BasicRequestInfo {
                    ClientVersion = clientVersion,
                    Version = appVersion,
                    Build = appBuild,
                    Platform = "IOS",
                    Country = "US",
                    Language = "en",
                    Debug = false
                };
                var body = await GetBAC(Convert.ToBase64String(request.ToByteArray()));
                var responseBytes = await PostRaw("ei/get_periodicals", body, HeaderProfile.Ios);
                return responseBytes is not null && IsValidPeriodicalsResponse(GetFromAuthenticatedMessage<PeriodicalsResponse>(responseBytes));
            } catch {
                return false;
            }
        }

        public Task<ContractCoopStatusResponse> GetCoopStatus(string contractName, string coopName, string eiid = null, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default) =>
            CoopStatusCore("ei/coop_status", HeaderProfile.CoopStatus, contractName, coopName, eiid ?? UserId, xrefs, logger ?? _logger, cancellationToken);

        public Task<ContractCoopStatusResponse> GetCoopStatusBot(string contractName, string coopName, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default) =>
            CoopStatusCore("ei/coop_status_bot", HeaderProfile.Android, contractName, coopName, "EI6291940968235008", xrefs, logger ?? _logger, cancellationToken);

        private async Task<ContractCoopStatusResponse> CoopStatusCore(string path, HeaderProfile profile, string contractName, string coopName, string eiid, List<UserCoopXref> xrefs, ILogger log, CancellationToken cancellationToken) {
            try {
                var model = new ContractCoopStatusRequest {
                    ContractIdentifier = contractName,
                    CoopIdentifier = coopName.ToLower(),
                    Rinfo = GetInfo(eiid),
                    UserId = eiid,
                    ClientVersion = ClientVersion,
                    ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                var body = await GetBAC(GetEncodedMessage(model));
                var responseBytes = await PostRaw(path, body, profile, cancellationToken);
                if(responseBytes == null)
                    return null;
                var coopStatus = GetFromAuthenticatedMessage<ContractCoopStatusResponse>(responseBytes);
                if(!string.Equals(coopStatus.CoopIdentifier, coopName, StringComparison.OrdinalIgnoreCase))
                    return null;
                coopStatus.Success = true;
                return FixDepartedUsers(coopStatus, xrefs);
            } catch(ArgumentNullException ex) {
                log.LogError(ex, "ArgumentNullException in coop status {Path} for {Coop}", path, coopName);
                return null;
            }
        }

        private static ContractCoopStatusResponse FixDepartedUsers(ContractCoopStatusResponse coopStatus, List<UserCoopXref> xrefs) {
            var filteredXrefs = xrefs?.Where(x => x.LastStatus?.ContributionAmount is not null && !string.IsNullOrEmpty(x.LastStatus?.UserName)).ToList();
            if(filteredXrefs == null)
                return coopStatus;
            foreach(var departedUser in coopStatus.Participants.Where(x => x.UserName == "[departed]")) {
                var matchingUser = filteredXrefs.FirstOrDefault(x => x.LastStatus.ContributionAmount == departedUser.ContributionAmount);
                if(matchingUser is not null)
                    departedUser.UserName = matchingUser.LastStatus.UserName;
            }
            return coopStatus;
        }

        public async Task<EggIncFirstContactResponse> FirstContact(string userId, ILogger logger = null) {
            var log = logger ?? _logger;
            var botResult = await BotFirstContact(userId);
            if(botResult is { Success: true })
                return botResult;

            if(!EggIncApiSecrets.IsSaltAvailable) {
                log.LogWarning("bot_first_contact failed for {UserId} ({Error}) and no salt is configured for first_contact_secure fallback", userId, botResult?.Error);
                return botResult;
            }
            var secureResult = await FirstContactSecure(userId);
            if(secureResult is not { Success: true })
                log.LogWarning("first_contact_secure also failed for {UserId} ({Error})", userId, secureResult?.Error);
            return secureResult;
        }

        public async Task<EggIncFirstContactResponse> BotFirstContact(string userId) {
            try {
                var request = new EggIncFirstContactRequest {
                    ClientVersion = ClientVersion,
                    Platform = Platform.Droid,
                    EiUserId = userId,
                    DeviceId = userId,
                    Username = "",
                    Rinfo = GetInfo(userId)
                };
                var body = await GetBAC(GetEncodedMessage(request));
                var responseBytes = await PostRaw("ei/bot_first_contact", body, HeaderProfile.Android);
                if(responseBytes == null)
                    return new EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                var backup = ParseTolerant<EggIncFirstContactResponse>(responseBytes);
                if(backup.Backup is null)
                    return new EggIncFirstContactResponse { Success = false, Error = "bot_first_contact returned no backup" };
                backup.Success = true;
                return backup;
            } catch(Exception e) {
                return new EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }

        private async Task<EggIncFirstContactResponse> FirstContactSecure(string userId) {
            if(!EggIncApiSecrets.IsSaltAvailable)
                return new EggIncFirstContactResponse { Success = false, Error = "Egg Inc API salt not configured; first_contact_secure disabled" };
            try {
                var isEi = userId.StartsWith("EI");
                var request = isEi
                    ? new EggIncFirstContactRequest { ClientVersion = ClientVersion, Platform = Platform.Droid, EiUserId = userId, DeviceId = userId, Username = "", Rinfo = GetInfo(userId) }
                    : new EggIncFirstContactRequest { ClientVersion = ClientVersion, Platform = Platform.Droid, UserId = userId };

                var messageData = request.ToByteArray();
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                var body = await GetBAC(GetEncodedMessage(authMessage));
                var responseBytes = await PostRaw("ei/first_contact_secure", body, HeaderProfile.Android);
                if(responseBytes == null)
                    return new EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                var backup = isEi
                    ? GetFromAuthenticatedMessage<EggIncFirstContactResponse>(responseBytes)
                    : ParseTolerant<EggIncFirstContactResponse>(responseBytes);
                backup.Success = true;
                return backup;
            } catch(Exception e) {
                return new EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }

        public Task<ApiResult<ContractsInfoResponse>> GetContractsInfoAsync(string userId, params string[] contractIdentifiers) {
            var request = new ContractsInfoRequest { ClientVersion = ClientVersion };
            request.ContractIdentifiers.AddRange(contractIdentifiers);
            return PostResult<ContractsInfoResponse, ContractsInfoRequest>(request, userId);
        }

        public Task<ApiResult<ContractsArchive>> GetContractsArchive(string userId) =>
            SignedAndroidAsync<ContractsArchive>("ei_ctx/get_contracts_archive", GetInfo(userId), sign: false);

        public Task<ApiResult<ContractSeasonInfos>> GetSeasonInfosAsync() =>
            SignedAndroidAsync<ContractSeasonInfos>("ei_ctx/get_season_infos_v2", GetInfo(UserId), sign: true);

        public Task<ApiResult<ContractPlayerInfo>> GetContractPlayerInfo(string userId) =>
            SignedAndroidAsync<ContractPlayerInfo>("ei_ctx/get_contract_player_info", GetInfo(userId), sign: true);

        private async Task<ApiResult<T>> SignedAndroidAsync<T>(string path, IMessage info, bool sign) where T : IMessage, new() {
            try {
                IMessage payload = info;
                if(sign) {
                    var messageData = info.ToByteArray();
                    payload = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                }
                var body = await GetBAC(GetEncodedMessage(payload));
                var (responseBytes, error) = await PostRawWithError(path, body, HeaderProfile.Android);
                if(responseBytes == null)
                    return ApiResult<T>.Fail(error ?? "No response");
                return GetFromAuthenticatedMessage<T>(responseBytes);
            } catch(Exception e) {
                return ApiResult<T>.Fail("Bot Exception: " + e.Message);
            }
        }

        public async Task<ApiResult<CustomBackup>> GetBackupAsync(string eggIncId, FrozenSet<Contract> cachedContracts) {
            var firstContact = await FirstContact(eggIncId);
            return firstContact.Success
                ? new CustomBackup(firstContact.Backup, cachedContracts, null)
                : ApiResult<CustomBackup>.Fail(firstContact.Error ?? "first_contact failed");
        }
    }
}
