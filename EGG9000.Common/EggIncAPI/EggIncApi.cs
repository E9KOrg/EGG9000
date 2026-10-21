using EggSharp.Api;
using EggSharp.Core;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public sealed partial class EggIncApi(EggSharp.Api.IEggIncApi api, ILogger<EggIncApi> logger) : IEggIncApi {
        private readonly ILogger<EggIncApi> _logger = logger;
        public const string UserId = "EI6145601714651136";
        public const string PeriodicalsReferenceUserId = "EI5482515761594368";
        private const string PeriodicalsPostUserId = "EI4765194876354560";
        private const string BotUserId = "EI6291940968235008";

        public static readonly List<(string EggIncId, Contract.Types.PlayerGrade Grade, string Name)> CoopCreatorIds = [];

        public static VersionTripleSource Versions { get; } = new(new VersionTriple(75, "1.35.6", "1.35.6.3"));

        public static uint ClientVersion {
            get => Versions.Current.ClientVersion;
            set => Versions.Set(Versions.Current with { ClientVersion = value });
        }

        public static string AppVersion => Versions.Current.AppVersion;
        public static string AppBuild => Versions.Current.AppBuild;

        public static void SetVersions(uint clientVersion, string appVersion, string appBuild) => Versions.Set(new VersionTriple(clientVersion, appVersion, appBuild));

        public static BasicRequestInfo GetInfo(string userId, bool noUserId = false) {
            var info = new BasicRequestInfo {
                ClientVersion = ClientVersion,
                Version = AppVersion,
                Build = AppBuild,
                Platform = "IOS",
                Country = "US",
                Language = "en",
                Debug = false
            };
            if(!noUserId)
                info.EiUserId = userId;
            return info;
        }

        private static ApiCallOptions Options(string userId, bool authenticated = false) => new(AuthenticatedResponse: authenticated ? true : null, UserId: userId);

        public Task<bool> Send<TRequest>(TRequest data, string userId) where TRequest : class, IMessage =>
            api.Send(data, Options(userId));

        public Task<TResponse> Post<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : class, IMessage<TResponse>, new() where TRequest : class, IMessage =>
            api.Post<TRequest, TResponse>(data, Options(userId, authenticated));

        public Task<ApiResult<TResponse>> PostResult<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : class, IMessage<TResponse>, new() where TRequest : class, IMessage =>
            api.PostResult<TRequest, TResponse>(data, Options(userId, authenticated));

        private async Task<ApiResult<T>> Result<T>(Func<Task<ApiOutcome<T>>> call) where T : class {
            try {
                var outcome = await call();
                return outcome.Success ? outcome.Response : ApiResult.Fail<T>(outcome.Error ?? api.Preset.Errors.NoResponse);
            } catch(Exception e) {
                return ApiResult.Fail<T>("Bot Exception: " + e.Message);
            }
        }

        public static TResponse ParseTolerant<TResponse>(byte[] responseBytes) where TResponse : IMessage<TResponse>, new() {
            var parser = new MessageParser<TResponse>(() => new TResponse());
            try {
                return parser.ParseFrom(responseBytes);
            } catch(Exception e) when(ProtobufUtf8Sanitizer.IsInvalidUtf8(e)) {
                return parser.ParseFrom(ProtobufUtf8Sanitizer.Sanitize(responseBytes));
            }
        }
    }
}
