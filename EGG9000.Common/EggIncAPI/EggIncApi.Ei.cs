using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public partial class EggIncApi {

        private const string PeriodicalsReferenceUserId = "EI5482515761594368";
        private const string PeriodicalsPostUserId = "EI4765194876354560";

        private static GetPeriodicalsRequest BuildPeriodicalsRequest(string userId, uint clientVersion) {
            return new GetPeriodicalsRequest {
                UserId = userId,
                PiggyFull = false,
                PiggyFoundFull = false,
                SecondsFullRealtime = 2339576.17448521,
                SecondsFullGametime = 391564.659540082,
                SoulEggs = 570149167.28294,
                CurrentClientVersion = clientVersion,
                Debug = false,
            };
        }

        public static async Task<PeriodicalsResponse> GetPeriodicalsAsync(string userId = PeriodicalsReferenceUserId) {
            return await Post<PeriodicalsResponse, GetPeriodicalsRequest>(BuildPeriodicalsRequest(userId, ClientVersion), PeriodicalsPostUserId, true);
        }

        public static bool IsValidPeriodicalsResponse(PeriodicalsResponse resp) {
            return (resp?.Contracts?.Contracts?.Count ?? 0) > 0;
        }

        // Validates a candidate version triple by issuing a periodicals call built entirely from the
        // candidate values, without mutating the live globals. Returns true only on a decodable
        // response carrying at least one contract. Any failure (rejection, empty, network) returns false.
        public static async Task<bool> ValidateVersionsAsync(uint clientVersion, string appVersion, string appBuild, string userId = PeriodicalsReferenceUserId) {
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
                if(responseBytes is null)
                    return false;
                return IsValidPeriodicalsResponse(GetFromAuthenticatedMessage<PeriodicalsResponse>(responseBytes));
            } catch {
                return false;
            }
        }

        public static async Task<ContractCoopStatusResponse> GetCoopStatus(string ContractName, string CoopName, string EIID = null, List<UserCoopXref> xrefs = null, ILogger _logger = null, CancellationToken cancellationToken = default) {
            try {
                var model = new ContractCoopStatusRequest {
                    ContractIdentifier = ContractName,
                    CoopIdentifier = CoopName.ToLower(),
                    Rinfo = GetInfo(EIID ?? UserId),
                    UserId = EIID ?? UserId,
                    ClientVersion = ClientVersion,
                    ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                var body = await GetBAC(GetEncodedMessage(model));
                var responseBytes = await PostRaw("ei/coop_status", body, HeaderProfile.CoopStatus, http2: true, cancellationToken);
                if(responseBytes == null) {
                    return null;
                }
                var coopStatus = GetFromAuthenticatedMessage<ContractCoopStatusResponse>(responseBytes);
                if(string.Equals(coopStatus.CoopIdentifier, CoopName, StringComparison.OrdinalIgnoreCase)) {
                    coopStatus.Success = true;
                    return FixDepartedUsers(coopStatus, xrefs);
                }
                return null;
            } catch(ArgumentNullException ex) {
                _logger?.LogError("ArgumentNullException in GetCoopStatus:\nParam Name: {pName}\n{message}\n{stackTrace}", ex.ParamName, ex.Message, ex.StackTrace);
                return null;
            }
        }

        public static async Task<ContractCoopStatusResponse> GetCoopStatusBot(string ContractName, string CoopName, List<UserCoopXref> xrefs = null, ILogger _logger = null, CancellationToken cancellationToken = default) {
            var EIID = "EI6291940968235008";
            try {
                var model = new ContractCoopStatusRequest {
                    ContractIdentifier = ContractName,
                    CoopIdentifier = CoopName.ToLower(),
                    Rinfo = GetInfo(EIID),
                    UserId = EIID,
                    ClientVersion = ClientVersion,
                    ClientTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                var body = await GetBAC(GetEncodedMessage(model));
                var responseBytes = await PostRaw("ei/coop_status_bot", body, HeaderProfile.Android, cancellationToken: cancellationToken);
                if(responseBytes == null) {
                    return null;
                }
                var coopStatus = GetFromAuthenticatedMessage<ContractCoopStatusResponse>(responseBytes);
                if(string.Equals(coopStatus.CoopIdentifier, CoopName, StringComparison.OrdinalIgnoreCase)) {
                    coopStatus.Success = true;
                    return FixDepartedUsers(coopStatus, xrefs);
                }
                return null;
            } catch(ArgumentNullException ex) {
                _logger?.LogError("ArgumentNullException in GetCoopStatusBot:\nParam Name: {pName}\n{message}\n{stackTrace}", ex.ParamName, ex.Message, ex.StackTrace);
                return null;
            }
        }

        private static ContractCoopStatusResponse FixDepartedUsers(ContractCoopStatusResponse coopStatus, List<UserCoopXref> xrefs) {
            var filteredXrefs = xrefs?.Where(
                x => x.LastStatus?.ContributionAmount is not null &&
                !string.IsNullOrEmpty(x.LastStatus?.UserName)
            ).ToList();

            if(filteredXrefs == null) return coopStatus;
            foreach(var departedUser in coopStatus.Participants.Where(x => x.UserName == "[departed]")) {
                var matchingUser = filteredXrefs.FirstOrDefault(x => x.LastStatus.ContributionAmount == departedUser.ContributionAmount);
                if(matchingUser is not null) departedUser.UserName = matchingUser.LastStatus.UserName;
            }
            return coopStatus;
        }

        /// <summary>
        /// Fetch a player's first-contact backup. Prefers the salt-free <c>ei/bot_first_contact</c>
        /// endpoint; falls back to the salt-signed <c>ei/first_contact_secure</c> only when the bot
        /// endpoint fails AND a salt is configured. With no salt the fallback self-cripples and the
        /// method returns the bot endpoint's failure response.
        /// </summary>
        public static async Task<EggIncFirstContactResponse> FirstContact(string UserId, ILogger _logger = null) {
            var botResult = await BotFirstContact(UserId);
            if(botResult is { Success: true }) {
                return botResult;
            }

            if(!EggIncApiSecrets.IsSaltAvailable) {
                _logger?.LogWarning("bot_first_contact failed for {UserId} ({Error}) and no salt is configured for first_contact_secure fallback", UserId, botResult?.Error);
                return botResult;
            }
            _logger?.LogInformation("bot_first_contact did not return a backup for {UserId} ({Error}); falling back to first_contact_secure", UserId, botResult?.Error);
            var secureResult = await FirstContactSecure(UserId);
            if(secureResult is { Success: true }) {
                _logger?.LogInformation("first_contact_secure recovered a backup for {UserId} that bot_first_contact missed", UserId);
            } else {
                _logger?.LogWarning("first_contact_secure also failed for {UserId} ({Error})", UserId, secureResult?.Error);
            }
            return secureResult;
        }

        /// <summary>
        /// Salt-free first contact via <c>ei/bot_first_contact</c>. The response is an unwrapped
        /// <see cref="EggIncFirstContactResponse"/> (response-authenticated = false), so it is parsed
        /// directly rather than through <see cref="GetFromAuthenticatedMessage{T}"/>.
        /// </summary>
        public static async Task<EggIncFirstContactResponse> BotFirstContact(string UserId) {
            try {
                var request = new EggIncFirstContactRequest {
                    ClientVersion = ClientVersion,
                    Platform = Platform.Droid,
                    EiUserId = UserId,
                    DeviceId = UserId,
                    Username = "",
                    Rinfo = GetInfo(UserId)
                };
                var body = await GetBAC(GetEncodedMessage(request));
                var responseBytes = await PostRaw("ei/bot_first_contact", body, HeaderProfile.Android);
                if(responseBytes == null) {
                    return new EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                }
                var backup = EggIncFirstContactResponse.Parser.ParseFrom(responseBytes);
                // bot_first_contact can return a parseable-but-empty response (no Backup) for accounts
                // the salt-signed endpoint still serves. Report failure so FirstContact falls through to
                // first_contact_secure instead of masking it as success-with-null-backup.
                if(backup.Backup is null) {
                    return new EggIncFirstContactResponse { Success = false, Error = "bot_first_contact returned no backup" };
                }
                backup.Success = true;
                return backup;
            } catch(Exception e) {
                return new EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }

        /// <summary>
        /// Legacy salt-signed first contact via <c>ei/first_contact_secure</c>, kept as a fallback.
        /// Self-cripples (returns a failure response) when no salt is configured.
        /// </summary>
        private static async Task<EggIncFirstContactResponse> FirstContactSecure(string UserId) {
            if(!EggIncApiSecrets.IsSaltAvailable) {
                return new EggIncFirstContactResponse { Success = false, Error = "Egg Inc API salt not configured; first_contact_secure disabled" };
            }
            try {
                var isEi = UserId.StartsWith("EI");
                var request = isEi
                    ? new EggIncFirstContactRequest { ClientVersion = ClientVersion, Platform = Platform.Droid, EiUserId = UserId, DeviceId = UserId, Username = "", Rinfo = GetInfo(UserId) }
                    : new EggIncFirstContactRequest { ClientVersion = ClientVersion, Platform = Platform.Droid, UserId = UserId };

                var messageData = request.ToByteArray();
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                var body = await GetBAC(GetEncodedMessage(authMessage));
                var responseBytes = await PostRaw("ei/first_contact_secure", body, HeaderProfile.Android);
                if(responseBytes == null) {
                    return new EggIncFirstContactResponse { Success = false, Error = "Error response from API" };
                }
                var backup = isEi
                    ? GetFromAuthenticatedMessage<EggIncFirstContactResponse>(responseBytes)
                    : EggIncFirstContactResponse.Parser.ParseFrom(responseBytes);
                backup.Success = true;
                return backup;
            } catch(Exception e) {
                return new EggIncFirstContactResponse { Success = false, Error = "Bot Exception: " + e.Message };
            }
        }

        public static async Task<ApiResult<ContractsInfoResponse>> GetContractsInfoAsync(string userId, params string[] contractIdentifiers) {
            var request = new ContractsInfoRequest { ClientVersion = ClientVersion };
            request.ContractIdentifiers.AddRange(contractIdentifiers);
            return await PostResult<ContractsInfoResponse, ContractsInfoRequest>(request, userId);
        }

        public static async Task<ApiResult<ContractsArchive>> GetContractsArchive(string UserId) {
            try {
                var request = GetInfo(UserId);
                var body = await GetBAC(GetEncodedMessage(request));
                var (responseBytes, error) = await PostRawWithError("ei_ctx/get_contracts_archive", body, HeaderProfile.Android);
                if(responseBytes == null) {
                    return ApiResult<ContractsArchive>.Fail(error ?? "No response");
                }
                return GetFromAuthenticatedMessage<ContractsArchive>(responseBytes);
            } catch(Exception e) {
                return ApiResult<ContractsArchive>.Fail("Bot Exception: " + e.Message);
            }
        }


        public static async Task<ApiResult<ContractSeasonInfos>> GetSeasonInfosAsync() {
            try {
                var info = GetInfo(UserId);
                var messageData = info.ToByteArray();
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                var body = await GetBAC(GetEncodedMessage(authMessage));
                var (responseBytes, error) = await PostRawWithError("ei_ctx/get_season_infos_v2", body, HeaderProfile.Android);
                if(responseBytes == null) {
                    return ApiResult<ContractSeasonInfos>.Fail(error ?? "No response");
                }
                return GetFromAuthenticatedMessage<ContractSeasonInfos>(responseBytes);
            } catch(Exception e) {
                return ApiResult<ContractSeasonInfos>.Fail("Bot Exception: " + e.Message);
            }
        }

        public static async Task<ApiResult<ContractPlayerInfo>> GetContractPlayerInfo(string UserId) {
            try {
                var info = GetInfo(UserId);


                var messageData = info.ToByteArray();
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(messageData), Code = GetHash(messageData) };
                var body = await GetBAC(GetEncodedMessage(authMessage));

                var (responseBytes, error) = await PostRawWithError("ei_ctx/get_contract_player_info", body, HeaderProfile.Android);
                if(responseBytes == null) {
                    return ApiResult<ContractPlayerInfo>.Fail(error ?? "No response");
                }
                return GetFromAuthenticatedMessage<ContractPlayerInfo>(responseBytes);
            } catch(Exception e) {
                return ApiResult<ContractPlayerInfo>.Fail("Bot Exception: " + e.Message);
            }
        }


        public static async Task<ApiResult<CustomBackup>> GetBackupAsync(string EggIncId, FrozenSet<Ei.Contract> cachedContracts) {
            var firstContact = await FirstContact(EggIncId);
            if(firstContact.Success) {
                return new CustomBackup(firstContact.Backup, cachedContracts, null);
            }
            return ApiResult<CustomBackup>.Fail(firstContact.Error ?? "first_contact failed");
        }
    }
}
