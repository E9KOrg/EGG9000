using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Ei;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public partial class EggIncApi {

        public static async Task<PeriodicalsResponse> GetPeriodicalsAsync() {
            return await Post<PeriodicalsResponse, GetPeriodicalsRequest>(new GetPeriodicalsRequest {
                UserId = "EI5482515761594368",
                PiggyFull = false,
                PiggyFoundFull = false,
                SecondsFullRealtime = 2339576.17448521,
                SecondsFullGametime = 391564.659540082,
                SoulEggs = 570149167.28294,
                CurrentClientVersion = ClientVersion,
                Debug = false,
            }, "EI4765194876354560", true);
        }

        public static async Task<ContractCoopStatusResponse> GetCoopStatus(string ContractName, string CoopName, string EIID = null, List<UserCoopXref> xrefs = null, ILogger _logger = null, CancellationToken cancellationToken = default) {
            try {
                var model = new ContractCoopStatusRequest {
                    ContractIdentifier = ContractName,
                    CoopIdentifier = CoopName.ToLower(),
                    Rinfo = GetInfo(EIID ?? UserId),
                    UserId = EIID ?? UserId,
                    ClientVersion = ClientVersion,
                    ClientTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
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
                    ClientTimestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
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
        public static async Task<EggIncFirstContactResponse> FirstContact(string UserId) {
            var botResult = await BotFirstContact(UserId);
            if(botResult is { Success: true }) {
                return botResult;
            }

            if(!EggIncApiSecrets.IsSaltAvailable) {
                return botResult;
            }
            return await FirstContactSecure(UserId);
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

        public static async Task<CustomBackup> GetBackupAsync(string EggIncId) {
            var firstContact = await FirstContact(EggIncId);
            if(firstContact.Success) {
                return new CustomBackup(firstContact.Backup, null);
            } else {
                return null;
            }
        }
    }
}
