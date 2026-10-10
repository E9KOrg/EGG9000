using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.EggIncAPI {

    public sealed partial class EggIncApi(IHttpClientFactory httpClientFactory, ILogger<EggIncApi> logger) : IEggIncApi {
        private readonly ILogger<EggIncApi> _logger = logger;
        public const string BaseAddressNew = "https://www.auxbrain.com/";
        public const string UserId = "EI6145601714651136";
        public const string PeriodicalsReferenceUserId = "EI5482515761594368";
        private const string PeriodicalsPostUserId = "EI4765194876354560";

        public static readonly List<(string EggIncId, Contract.Types.PlayerGrade Grade, string Name)> CoopCreatorIds = [];

        public static uint ClientVersion { get; set; } = 75;
        public static string AppVersion { get; set; } = "1.35.6";
        public static string AppBuild { get; set; } = "1.35.6.3";

        public static void SetVersions(uint clientVersion, string appVersion, string appBuild) {
            ClientVersion = clientVersion;
            AppVersion = appVersion;
            AppBuild = appBuild;
        }

        private const string AndroidUserAgent = "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)";
        private const string IosUserAgent = "egginc/1.26.1.3 CFNetwork/1335.0.3 Darwin/21.6.0";
        private const string CoopStatusUserAgent = "egginc/1.35.3.1 CFNetwork/1410.1 Darwin/22.6.0";

        public const string AndroidClient = "EggInc.Android";
        public const string IosClient = "EggInc.Ios";
        public const string CoopStatusClient = "EggInc.CoopStatus";

        private const int DefaultApiTimeoutSeconds = 30;

        internal static void ConfigureAndroidClient(HttpClient client) => Configure(client, HeaderProfile.Android, false);
        internal static void ConfigureIosClient(HttpClient client) => Configure(client, HeaderProfile.Ios, false);
        internal static void ConfigureCoopStatusClient(HttpClient client) => Configure(client, HeaderProfile.CoopStatus, true);

        private static void Configure(HttpClient client, HeaderProfile profile, bool http2) {
            client.BaseAddress = new Uri(BaseAddressNew);
            client.Timeout = TimeSpan.FromSeconds(DefaultApiTimeoutSeconds);
            if(http2)
                client.DefaultRequestVersion = HttpVersion.Version20;
            switch(profile) {
                case HeaderProfile.Android:
                    client.DefaultRequestHeaders.Add("User-Agent", AndroidUserAgent);
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    break;
                case HeaderProfile.CoopStatus:
                    client.DefaultRequestHeaders.Add("cookie", "session=9cd692e4-050e-4cb9-a305-993bd28441b2");
                    client.DefaultRequestHeaders.Add("user-agent", CoopStatusUserAgent);
                    client.DefaultRequestHeaders.Add("accept-encoding", "gzip, deflate, br");
                    client.DefaultRequestHeaders.Add("accept-language", "en-US,en;q=0.9");
                    client.DefaultRequestHeaders.Add("accept", "*/*");
                    break;
                default:
                    client.DefaultRequestHeaders.Add("User-Agent", IosUserAgent);
                    client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    client.DefaultRequestHeaders.Add("Connection", "Keep-Alive");
                    break;
            }
        }

        private static readonly ResiliencePipeline<HttpResponseMessage> _pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage> {
                MaxRetryAttempts = 2,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TimeoutRejectedException>()
                    .HandleResult(r => (int)r.StatusCode >= 500 || r.StatusCode == HttpStatusCode.TooManyRequests)
            })
            .AddTimeout(TimeSpan.FromSeconds(DefaultApiTimeoutSeconds))
            .Build();

        private HttpClient ClientFor(HeaderProfile profile) => httpClientFactory.CreateClient(profile switch {
            HeaderProfile.Android => AndroidClient,
            HeaderProfile.CoopStatus => CoopStatusClient,
            _ => IosClient
        });

        private async Task<HttpResponseMessage> PostCounted(HeaderProfile profile, string path, HttpContent body, CancellationToken cancellationToken) {
            Services.RuntimeMetrics.AddApiCalls();
            try {
                return await _pipeline.ExecuteAsync(async ct => await ClientFor(profile).PostAsync(path, body, ct), cancellationToken);
            } catch(Exception e) when(e is not OperationCanceledException || !cancellationToken.IsCancellationRequested) {
                Services.RuntimeMetrics.AddApiFailures();
                _logger.LogWarning(e, "Egg Inc API call to {Path} failed after retries", path);
                throw;
            }
        }

        private async Task<byte[]> PostRaw(string path, ByteArrayContent body, HeaderProfile profile, CancellationToken cancellationToken = default) {
            using var response = await PostCounted(profile, path, body, cancellationToken);
            if(!response.IsSuccessStatusCode)
                return null;
            return Convert.FromBase64String(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        private async Task<(byte[] Bytes, string Error)> PostRawWithError(string path, ByteArrayContent body, HeaderProfile profile, CancellationToken cancellationToken = default) {
            using var response = await PostCounted(profile, path, body, cancellationToken);
            if(!response.IsSuccessStatusCode)
                return (null, $"HTTP Error: {response.StatusCode}");
            return (Convert.FromBase64String(await response.Content.ReadAsStringAsync(cancellationToken)), null);
        }

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

        public static string GetEncodedMessage(IMessage message) => Convert.ToBase64String(message.ToByteArray());

        public static async Task<ByteArrayContent> GetBAC(string base64) {
            var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", base64)]);
            var bytes = await content.ReadAsByteArrayAsync();
            return new ByteArrayContent(bytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded") } };
        }

        private enum HeaderProfile { Android, Ios, CoopStatus }
        private enum RinfoMode { None, WithUser, WithoutUser }

        private sealed record EndpointDescriptor(string Path, HeaderProfile Headers, RinfoMode Rinfo, bool SignRequest, bool AuthenticatedResponse);

        private static readonly Dictionary<Type, EndpointDescriptor> Endpoints = new() {
            [typeof(JoinCoopRequest)] = new("ei/join_coop", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(GetPeriodicalsRequest)] = new("ei/get_periodicals", HeaderProfile.Ios, RinfoMode.WithoutUser, false, true),
            [typeof(ContractsInfoRequest)] = new("ei_ctx/get_contracts_info", HeaderProfile.Ios, RinfoMode.WithUser, true, true),
            [typeof(CreateCoopRequest)] = new("ei/create_coop", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(UpdateCoopPermissionsRequest)] = new("ei/update_coop_permissions", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(ContractCoopStatusUpdateRequest)] = new("ei/update_coop_status_secure", HeaderProfile.Ios, RinfoMode.WithUser, true, false),
            [typeof(ConfigRequest)] = new("ei/get_config", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(KickPlayerCoopRequest)] = new("ei/kick_player_coop", HeaderProfile.Android, RinfoMode.None, false, false),
        };

        private static EndpointDescriptor ResolveEndpoint(Type requestType, Type responseType) {
            if(requestType == typeof(BasicRequestInfo)) {
                if(responseType == typeof(ContractPlayerInfo))
                    return new("ei_ctx/get_contract_player_info", HeaderProfile.Ios, RinfoMode.None, true, true);
                if(responseType == typeof(MyContracts))
                    return new("ei_ctx/get_contracts_archive", HeaderProfile.Ios, RinfoMode.None, false, true);
                return null;
            }
            return Endpoints.GetValueOrDefault(requestType);
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo> _rinfoProps = new();

        private static void SetRinfo(IMessage data, BasicRequestInfo info) {
            var prop = _rinfoProps.GetOrAdd(data.GetType(), t => t.GetProperty("Rinfo"));
            prop?.SetValue(data, info);
        }

        private int _saltWarned;
        private void WarnSaltUnavailableOnce(string path) {
            if(Interlocked.Exchange(ref _saltWarned, 1) == 0)
                _logger.LogError("Salt not configured; authenticated endpoint {Path} is disabled. Set the egg_inc_api_salt Docker secret or ConnectionStrings:ApiSalt.", path);
        }

        private static string BuildPayload(IMessage data, string userId, EndpointDescriptor d) {
            byte[] inner;
            if(data is BasicRequestInfo) {
                inner = GetInfo(userId).ToByteArray();
            } else {
                if(d.Rinfo != RinfoMode.None)
                    SetRinfo(data, GetInfo(userId, d.Rinfo == RinfoMode.WithoutUser));
                inner = data.ToByteArray();
            }
            if(d.SignRequest) {
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(inner), Code = GetHash(inner) };
                inner = authMessage.ToByteArray();
            }
            return Convert.ToBase64String(inner);
        }

        public async Task<bool> Send<TRequest>(TRequest data, string userId) where TRequest : IMessage {
            var descriptor = ResolveEndpoint(typeof(TRequest), null) ?? throw new InvalidOperationException($"Missing endpoint for {typeof(TRequest).Name}");
            if(descriptor.SignRequest && !EggIncApiSecrets.IsSaltAvailable) {
                WarnSaltUnavailableOnce(descriptor.Path);
                return false;
            }
            try {
                var bac = await GetBAC(BuildPayload(data, userId, descriptor));
                using var response = await PostCounted(descriptor.Headers, descriptor.Path, bac, CancellationToken.None);
                return response.IsSuccessStatusCode;
            } catch(Exception) {
                return false;
            }
        }

        public async Task<TResponse> Post<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : IMessage {
            var descriptor = ResolveEndpoint(typeof(TRequest), typeof(TResponse)) ?? throw new InvalidOperationException($"Missing endpoint for {typeof(TRequest).Name}");
            if(descriptor.SignRequest && !EggIncApiSecrets.IsSaltAvailable) {
                WarnSaltUnavailableOnce(descriptor.Path);
                return default;
            }
            try {
                var body = await GetBAC(BuildPayload(data, userId, descriptor));
                var responseBytes = await PostRaw(descriptor.Path, body, descriptor.Headers);
                if(responseBytes == null)
                    return default;
                return descriptor.AuthenticatedResponse || authenticated
                    ? GetFromAuthenticatedMessage<TResponse>(responseBytes)
                    : ParseTolerant<TResponse>(responseBytes);
            } catch(Exception) {
                return default;
            }
        }

        public async Task<ApiResult<TResponse>> PostResult<TResponse, TRequest>(TRequest data, string userId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : IMessage {
            var descriptor = ResolveEndpoint(typeof(TRequest), typeof(TResponse)) ?? throw new InvalidOperationException($"Missing endpoint for {typeof(TRequest).Name}");
            if(descriptor.SignRequest && !EggIncApiSecrets.IsSaltAvailable) {
                WarnSaltUnavailableOnce(descriptor.Path);
                return ApiResult<TResponse>.Fail($"API salt not configured for {descriptor.Path}");
            }
            try {
                var body = await GetBAC(BuildPayload(data, userId, descriptor));
                var (responseBytes, error) = await PostRawWithError(descriptor.Path, body, descriptor.Headers);
                if(responseBytes == null)
                    return ApiResult<TResponse>.Fail(error ?? "No response");
                return descriptor.AuthenticatedResponse || authenticated
                    ? GetFromAuthenticatedMessage<TResponse>(responseBytes)
                    : ParseTolerant<TResponse>(responseBytes);
            } catch(Exception e) {
                return ApiResult<TResponse>.Fail("Bot Exception: " + e.Message);
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

        private static void MergeTolerant<T>(T message, byte[] bytes) where T : IMessage {
            try {
                message.MergeFrom(bytes);
            } catch(Exception e) when(ProtobufUtf8Sanitizer.IsInvalidUtf8(e)) {
                message.MergeFrom(ProtobufUtf8Sanitizer.Sanitize(bytes));
            }
        }

        public static T GetFromAuthenticatedMessage<T>(byte[] authenticatedMessage) where T : IMessage, new() {
            var authMessageDecoded = AuthenticatedMessage.Parser.ParseFrom(authenticatedMessage);
            var message = new T();
            if(authMessageDecoded.Compressed) {
                using var outMemoryStream = new MemoryStream();
                using Stream inMemoryStream = new MemoryStream([.. authMessageDecoded.Message]);
                using var zlib = new ZLibStream(inMemoryStream, CompressionMode.Decompress);
                zlib.CopyTo(outMemoryStream);
                MergeTolerant(message, outMemoryStream.ToArray());
            } else {
                MergeTolerant(message, authMessageDecoded.Message.ToByteArray());
            }
            return message;
        }

        public static string GetHash(byte[] byteArray) {
            var phrase = EggIncApiSecrets.Salt;
            if(string.IsNullOrEmpty(phrase))
                throw new InvalidOperationException("Egg Inc API salt is not configured (set the egg_inc_api_salt Docker secret or ConnectionStrings:ApiSalt). Authenticated requests are disabled.");
            var magic = 0x3b9af419;
            var salt = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(phrase))));
            byteArray[magic % byteArray.Length] = 0x1b;
            return Convert.ToHexStringLower(SHA256.HashData([.. byteArray.Concat(salt)]));
        }
    }
}
