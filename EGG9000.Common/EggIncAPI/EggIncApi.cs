using Ei;

using Google.Protobuf;

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

    public partial class EggIncApi {
        public const string BaseAddressNew = "https://www.auxbrain.com/";
        public const string UserId = "EI6145601714651136";

        public static readonly List<(string EggIncId, Ei.Contract.Types.PlayerGrade Grade, string Name)> CoopCreatorIds = [];

        public static uint ClientVersion { get; set; } = 71;
        public const string AppVersion = "1.35.3";
        public const string AppBuild = "1.35.3.1";

        private const string AndroidUserAgent = "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)";
        private const string IosUserAgent = "egginc/1.26.1.3 CFNetwork/1335.0.3 Darwin/21.6.0";
        private const string CoopStatusUserAgent = "egginc/1.35.3.1 CFNetwork/1410.1 Darwin/22.6.0";

        public static BasicRequestInfo GetInfo(string UserId, bool noUserID = false) {
            var info = new BasicRequestInfo {
                ClientVersion = ClientVersion,
                Version = AppVersion,
                Build = AppBuild,
                Platform = "IOS",
                Country = "US",
                Language = "en",
                Debug = false
            };
            if(!noUserID) {
                info.EiUserId = UserId;
            }
            return info;
        }

        public static string GetEncodedMessage(IMessage message) {
            var ms1 = new MemoryStream();
            message.WriteTo(ms1);
            var base64 = Convert.ToBase64String(ms1.ToArray());
            return base64;
        }

        public static async Task<ByteArrayContent> GetBAC(string base64) {
            var content = new FormUrlEncodedContent([
                new KeyValuePair<string, string>("data", base64)
            ]);
            var bytes = await content.ReadAsByteArrayAsync();

            return new ByteArrayContent(bytes) { Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-www-form-urlencoded") } };
        }

        private enum HeaderProfile { Android, Ios, CoopStatus }
        private enum RinfoMode { None, WithUser, WithoutUser }

        /// <param name="Path">Endpoint path under the base address.</param>
        /// <param name="Headers">Which User-Agent/header set the live endpoint expects.</param>
        /// <param name="Rinfo">How to populate the request's <c>Rinfo</c> field (None = leave as sent).</param>
        /// <param name="SignRequest">Wrap the request in an <c>AuthenticatedMessage</c> (needs the salt).</param>
        /// <param name="AuthenticatedResponse">Decode the response via <see cref="GetFromAuthenticatedMessage{T}"/>.</param>
        private sealed record EndpointDescriptor(string Path, HeaderProfile Headers, RinfoMode Rinfo, bool SignRequest, bool AuthenticatedResponse);

        private static readonly Dictionary<Type, EndpointDescriptor> Endpoints = new() {
            // Post endpoints (iOS headers, Rinfo carried in the payload)
            [typeof(JoinCoopRequest)] = new("ei/join_coop", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(GetPeriodicalsRequest)] = new("ei/get_periodicals", HeaderProfile.Ios, RinfoMode.WithoutUser, false, true),
            [typeof(CreateCoopRequest)] = new("ei/create_coop", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(UpdateCoopPermissionsRequest)] = new("ei/update_coop_permissions", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(ContractCoopStatusUpdateRequest)] = new("ei/update_coop_status", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            [typeof(ConfigRequest)] = new("ei/get_config", HeaderProfile.Ios, RinfoMode.WithUser, false, false),
            // Send (fire-and-forget) endpoints (Android headers; legacy behavior sent no Rinfo in the payload)
            [typeof(KickPlayerCoopRequest)] = new("ei/kick_player_coop", HeaderProfile.Android, RinfoMode.None, false, false),
        };

        // BasicRequestInfo is the request body itself and resolves by response type.
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

        private static int _saltWarned;
        private static void WarnSaltUnavailableOnce(string path) {
            if(System.Threading.Interlocked.Exchange(ref _saltWarned, 1) == 0) {
                Console.Error.WriteLine(
                    $"[EggIncAPI] Salt not configured; authenticated endpoint '{path}' is disabled and will return no data. " +
                    "Set the 'egg_inc_api_salt' Docker secret or the 'ConnectionStrings:ApiSalt' configuration key to enable it.");
            }
        }

        private static HttpClient NewClient(bool http2 = false) {
            var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate };
            var client = new HttpClient(handler) { BaseAddress = new Uri(BaseAddressNew) };
            if(http2) {
                client.DefaultRequestVersion = HttpVersion.Version20;
            }
            return client;
        }

        private static void ApplyHeaders(HttpClient client, HeaderProfile profile) {
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

        // The single HTTP path for every Egg Inc call: new client, header profile, POST, and
        // base64-decode the body. Returns null on a non-success status. body may be null.
        private static async Task<byte[]> PostRaw(string path, ByteArrayContent body, HeaderProfile profile, bool http2 = false, CancellationToken cancellationToken = default) {
            using var client = NewClient(http2);
            ApplyHeaders(client, profile);
            var response = await client.PostAsync(path, body, cancellationToken);
            if(!response.IsSuccessStatusCode) {
                return null;
            }
            return Convert.FromBase64String(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        // Builds the base64 form payload: populates Rinfo (or replaces a BasicRequestInfo body with
        // GetInfo), then signs into an AuthenticatedMessage when the endpoint requires it.
        private static string BuildPayload(IMessage data, string userId, EndpointDescriptor d) {
            byte[] inner;
            if(data is BasicRequestInfo) {
                inner = GetInfo(userId).ToByteArray();
            } else {
                if(d.Rinfo != RinfoMode.None) {
                    SetRinfo(data, GetInfo(userId, d.Rinfo == RinfoMode.WithoutUser));
                }
                inner = data.ToByteArray();
            }
            if(d.SignRequest) {
                var authMessage = new AuthenticatedMessage { Message = ByteString.CopyFrom(inner), Code = GetHash(inner) };
                inner = authMessage.ToByteArray();
            }
            return Convert.ToBase64String(inner);
        }

        public static async Task<bool> Send<T2>(T2 data, string UserId) where T2 : IMessage {
            var descriptor = ResolveEndpoint(typeof(T2), null) ?? throw new Exception($"Missing Info for {typeof(T2).Name}");
            if(descriptor.SignRequest && !EggIncApiSecrets.IsSaltAvailable) {
                WarnSaltUnavailableOnce(descriptor.Path);
                return false;
            }
            try {
                var bac = await GetBAC(BuildPayload(data, UserId, descriptor));
                using var client = NewClient();
                ApplyHeaders(client, descriptor.Headers);
                var response = await client.PostAsync(descriptor.Path, bac);
                return response.IsSuccessStatusCode;
            } catch(Exception) {
                return false;
            }
        }

        public static async Task<TResponse> Post<TResponse, TRequest>(TRequest data, string UserId, bool authenticated = false) where TResponse : IMessage<TResponse>, new() where TRequest : IMessage {
            var descriptor = ResolveEndpoint(typeof(TRequest), typeof(TResponse)) ?? throw new Exception($"Missing Info for {typeof(TRequest).Name}");
            if(descriptor.SignRequest && !EggIncApiSecrets.IsSaltAvailable) {
                WarnSaltUnavailableOnce(descriptor.Path);
                return default;
            }
            try {
                var body = await GetBAC(BuildPayload(data, UserId, descriptor));
                var responseBytes = await PostRaw(descriptor.Path, body, descriptor.Headers);
                if(responseBytes == null) {
                    return default;
                }
                if(descriptor.AuthenticatedResponse || authenticated) {
                    return GetFromAuthenticatedMessage<TResponse>(responseBytes);
                }
                return new MessageParser<TResponse>(() => new TResponse()).ParseFrom(responseBytes);
            } catch(Exception) {
                return default;
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
                var arr = outMemoryStream.ToArray();
                var base64 = Encoding.UTF8.GetString(arr);
                message.MergeFrom(outMemoryStream.ToArray());
            } else {
                message.MergeFrom(authMessageDecoded.Message);
            }
            return message;
        }

        public static string GetHash(byte[] byteArray) {
            var phrase = EggIncApiSecrets.Salt;
            if(string.IsNullOrEmpty(phrase)) {
                throw new InvalidOperationException(
                    "Egg Inc API salt is not configured (set the 'egg_inc_api_salt' Docker secret or the " +
                    "'ConnectionStrings:ApiSalt' configuration key). Authenticated requests are disabled.");
            }
            var _magic = 0x3b9af419;
            var _salt = Encoding.ASCII.GetBytes(ByteArrayToString(SHA256.HashData(Encoding.ASCII.GetBytes(phrase))));
            byteArray[_magic % byteArray.Length] = 0x1b;
            return ByteArrayToString(SHA256.HashData([.. byteArray.Concat(_salt)]));
        }

        private static string ByteArrayToString(byte[] ba) {
            var hex = new StringBuilder(ba.Length * 2);
            foreach(var b in ba)
                hex.AppendFormat("{0:x2}", b);
            return hex.ToString();
        }

    }
}
