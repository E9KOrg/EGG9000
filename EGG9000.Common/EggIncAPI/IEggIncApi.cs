using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {
    public interface IEggIncApi {
        Task<bool> Send<TRequest>(TRequest data, string userId) where TRequest : IMessage;
        Task<TResponse> Post<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : IMessage;
        Task<ApiResult<TResponse>> PostResult<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : IMessage;

        Task<PeriodicalsResponse> GetPeriodicalsAsync(string userId = EggIncApi.PeriodicalsReferenceUserId);
        Task<bool> ValidateVersionsAsync(uint clientVersion, string appVersion, string appBuild, string userId = EggIncApi.PeriodicalsReferenceUserId);
        Task<ContractCoopStatusResponse> GetCoopStatus(string contractName, string coopName, string eiid = null, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default);
        Task<ContractCoopStatusResponse> GetCoopStatusBot(string contractName, string coopName, List<UserCoopXref> xrefs = null, ILogger logger = null, CancellationToken cancellationToken = default);
        Task<EggIncFirstContactResponse> FirstContact(string userId, ILogger logger = null);
        Task<EggIncFirstContactResponse> BotFirstContact(string userId);
        Task<ApiResult<ContractsInfoResponse>> GetContractsInfoAsync(string userId, params string[] contractIdentifiers);
        Task<ApiResult<ContractsArchive>> GetContractsArchive(string userId);
        Task<ApiResult<ContractSeasonInfos>> GetSeasonInfosAsync();
        Task<ApiResult<ContractPlayerInfo>> GetContractPlayerInfo(string userId);
        Task<ApiResult<CustomBackup>> GetBackupAsync(string eggIncId, FrozenSet<Contract> cachedContracts);
        Task<ApiResult<UserSubscriptionInfo>> GetUserSubscription(string userId);
    }
}
