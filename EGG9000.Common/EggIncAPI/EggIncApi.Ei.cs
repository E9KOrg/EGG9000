using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EggSharp.Api;
using EggSharp.Core;
using Ei;
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

        public Task<bool> ValidateVersionsAsync(uint clientVersion, string appVersion, string appBuild, string userId = PeriodicalsReferenceUserId) =>
            api.ValidateVersions(new VersionTriple(clientVersion, appVersion, appBuild), BuildPeriodicalsRequest(userId, clientVersion));

        public async Task<ContractCoopStatusResponse> GetCoopStatus(string contractName, string coopName, string eiid = null, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default) =>
            CoopStatusResult(await api.GetCoopStatus(contractName, coopName, eiid ?? UserId, cancellationToken: cancellationToken), coopName, xrefs, logger);

        public async Task<ContractCoopStatusResponse> GetCoopStatusBot(string contractName, string coopName, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default) =>
            CoopStatusResult(await api.GetCoopStatusBot(contractName, coopName, BotUserId, cancellationToken: cancellationToken), coopName, xrefs, logger);

        private ContractCoopStatusResponse CoopStatusResult(ApiOutcome<ContractCoopStatusResponse> outcome, string coopName, List<UserCoopXref> xrefs, ILogger logger) {
            if(!outcome.Success) {
                if(outcome.Error?.StartsWith("Bot Exception", StringComparison.Ordinal) is true)
                    (logger ?? _logger).LogError("Coop status for {Coop} failed: {Error}", coopName, outcome.Error);
                return null;
            }
            outcome.Response.Success = true;
            return FixDepartedUsers(outcome.Response, xrefs);
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
            try {
                return FirstContactResult(await api.FirstContact(userId, options: new ApiCallOptions(Logger: logger ?? _logger)));
            } catch(Exception e) {
                return FirstContactFailure("Bot Exception: " + e.Message);
            }
        }

        public async Task<EggIncFirstContactResponse> BotFirstContact(string userId) {
            try {
                var outcome = await api.BotFirstContact(userId);
                if(!outcome.Success)
                    return FirstContactFailure(outcome.StatusCode is null ? outcome.Error : api.Preset.Errors.ErrorResponse);
                if(outcome.Response.Backup is null)
                    return FirstContactFailure(api.Preset.Errors.NoBackup);
                return FirstContactResult(outcome);
            } catch(Exception e) {
                return FirstContactFailure("Bot Exception: " + e.Message);
            }
        }

        private static EggIncFirstContactResponse FirstContactResult(ApiOutcome<EggIncFirstContactResponse> outcome) {
            var response = outcome.Response ?? new EggIncFirstContactResponse();
            response.Success = outcome.Success;
            response.Error = outcome.Error;
            return response;
        }

        private static EggIncFirstContactResponse FirstContactFailure(string error) => new() { Success = false, Error = error };

        public Task<ApiResult<ContractsInfoResponse>> GetContractsInfoAsync(string userId, params string[] contractIdentifiers) {
            var request = new ContractsInfoRequest { ClientVersion = ClientVersion };
            request.ContractIdentifiers.AddRange(contractIdentifiers);
            return PostResult<ContractsInfoResponse, ContractsInfoRequest>(request, userId);
        }

        public Task<ApiResult<ContractsArchive>> GetContractsArchive(string userId) =>
            Result(() => api.GetContractsArchive(Options(userId)));

        public Task<ApiResult<ContractSeasonInfos>> GetSeasonInfosAsync() =>
            Result(() => api.GetSeasonInfos(Options(UserId)));

        public Task<ApiResult<ContractPlayerInfo>> GetContractPlayerInfo(string userId) =>
            Result(() => api.GetContractPlayerInfo(Options(userId)));

        public Task<ApiResult<UserSubscriptionInfo>> GetUserSubscription(string userId) =>
            Result(() => api.GetUserSubscription(userId));

        public async Task<ApiResult<CustomBackup>> GetBackupAsync(string eggIncId, FrozenSet<Contract> cachedContracts) {
            var firstContact = await FirstContact(eggIncId);
            return firstContact.Success
                ? new CustomBackup(firstContact.Backup, cachedContracts, null)
                : ApiResult.Fail<CustomBackup>(firstContact.Error ?? "first_contact failed");
        }
    }
}
