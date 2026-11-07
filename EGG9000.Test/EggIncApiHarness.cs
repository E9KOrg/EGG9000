using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;

using Google.Protobuf;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Concurrent;
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


namespace EGG9000.Test {
    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, Version Version, IReadOnlyDictionary<string, string> Headers, string? ContentType, string? Body) {
        public byte[] Payload => Body is { } body && body.StartsWith("data=", StringComparison.Ordinal)
            ? Convert.FromBase64String(Uri.UnescapeDataString(body[5..]))
            : throw new InvalidDataException($"Body is not a data= form: {Body}");

        public T Message<T>() where T : IMessage<T>, new() => new MessageParser<T>(() => new T()).ParseFrom(Payload);

        public T SignedMessage<T>() where T : IMessage<T>, new() => new MessageParser<T>(() => new T()).ParseFrom(Ei.AuthenticatedMessage.Parser.ParseFrom(Payload).Message);
    }

    internal sealed class RecordingHandler : HttpMessageHandler {
        private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

        public List<RecordedRequest> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Fallback { get; set; } = Wire.Ok("");

        public void Enqueue(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) {
            foreach(var response in responses)
                _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            lock(Requests)
                Requests.Add(new(request.Method, request.RequestUri!, request.Version, Wire.ParseHeaders(request.Headers.ToString()), request.Content?.Headers.ContentType?.ToString(), body));
            var respond = _responses.TryDequeue(out var next) ? next : Fallback;
            return respond(request);
        }
    }

    internal static class Wire {
        public const string Origin = "https://www.auxbrain.com/";
        public const string FormContentType = "application/x-www-form-urlencoded";

        public static readonly Version Http11 = HttpVersion.Version11;
        public static readonly Version Http20 = HttpVersion.Version20;

        public static IReadOnlyDictionary<string, string> AndroidHeaders => MasterHeaders(
            ("User-Agent", "Dalvik/2.1.0 (Linux; U; Android 9; SM-G960U1 Build/PPR1.180610.011)"),
            ("Accept-Encoding", "gzip"),
            ("Connection", "Keep-Alive"));

        public static IReadOnlyDictionary<string, string> IosHeaders => MasterHeaders(
            ("User-Agent", "egginc/1.26.1.3 CFNetwork/1335.0.3 Darwin/21.6.0"),
            ("Accept-Encoding", "gzip, deflate, br"),
            ("Connection", "Keep-Alive"));

        public static IReadOnlyDictionary<string, string> CoopStatusHeaders => MasterHeaders(
            ("cookie", "session=9cd692e4-050e-4cb9-a305-993bd28441b2"),
            ("user-agent", "egginc/1.35.3.1 CFNetwork/1410.1 Darwin/22.6.0"),
            ("accept-encoding", "gzip, deflate, br"),
            ("accept-language", "en-US,en;q=0.9"),
            ("accept", "*/*"));

        public static IReadOnlyDictionary<string, string> MasterHeaders(params (string Name, string Value)[] headers) {
            using var message = new HttpRequestMessage();
            foreach(var (name, value) in headers)
                message.Headers.Add(name, value);
            return ParseHeaders(message.Headers.ToString());
        }

        public static IReadOnlyDictionary<string, string> ParseHeaders(string text) => text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split(": ", 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.OrdinalIgnoreCase);

        public static void AssertHeaders(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual) {
            CollectionAssert.AreEquivalent(expected.Keys.Select(k => k.ToLowerInvariant()).ToList(), actual.Keys.Select(k => k.ToLowerInvariant()).ToList());
            foreach(var (name, value) in expected)
                Assert.AreEqual(value, actual[name], name);
        }

        public static async Task<string> MasterBody(byte[] payload) {
            var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", Convert.ToBase64String(payload))]);
            return await content.ReadAsStringAsync();
        }

        public static byte[] MasterSigned(byte[] inner) => new Ei.AuthenticatedMessage {
            Message = ByteString.CopyFrom(inner),
            Code = MasterHash(inner, EggIncApiHarness.Salt)
        }.ToByteArray();

        public static string MasterHash(byte[] message, string phrase) {
            var bytes = (byte[])message.Clone();
            var salt = Encoding.ASCII.GetBytes(Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(phrase))));
            bytes[0x3b9af419 % bytes.Length] = 0x1b;
            return Convert.ToHexStringLower(SHA256.HashData([.. bytes, .. salt]));
        }

        public static Ei.BasicRequestInfo MasterInfo(string? userId, uint clientVersion = EggIncApiHarness.ClientVersion, string version = EggIncApiHarness.AppVersion, string build = EggIncApiHarness.AppBuild) {
            var info = new Ei.BasicRequestInfo {
                ClientVersion = clientVersion,
                Version = version,
                Build = build,
                Platform = "IOS",
                Country = "US",
                Language = "en",
                Debug = false
            };
            if(userId is not null)
                info.EiUserId = userId;
            return info;
        }

        public static Func<HttpRequestMessage, HttpResponseMessage> Ok(string body) => _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };

        public static Func<HttpRequestMessage, HttpResponseMessage> Plain(IMessage message) => Ok(Convert.ToBase64String(message.ToByteArray()));

        public static Func<HttpRequestMessage, HttpResponseMessage> Authenticated(IMessage message) => Ok(Convert.ToBase64String(new Ei.AuthenticatedMessage {
            Message = message.ToByteString()
        }.ToByteArray()));

        public static Func<HttpRequestMessage, HttpResponseMessage> AuthenticatedZlib(IMessage message) {
            var plain = message.ToByteArray();
            using var output = new MemoryStream();
            using(var zlib = new ZLibStream(output, CompressionLevel.Optimal))
                zlib.Write(plain, 0, plain.Length);
            return Ok(Convert.ToBase64String(new Ei.AuthenticatedMessage {
                Message = ByteString.CopyFrom(output.ToArray()),
                Compressed = true,
                OriginalSize = (uint)plain.Length
            }.ToByteArray()));
        }

        public static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode code) => _ => new HttpResponseMessage(code) { Content = new StringContent("") };

        public static Func<HttpRequestMessage, HttpResponseMessage> Throw(string message) => _ => throw new HttpRequestException(message);
    }

    internal sealed class ImmediateDelayTimeProvider : TimeProvider {
        private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(20);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            TimeProvider.System.CreateTimer(callback, state, dueTime > TimeSpan.Zero && dueTime < Threshold ? TimeSpan.Zero : dueTime, period);
    }

    internal sealed class EggIncApiHarness : IDisposable {
        public const string Salt = "egg9000-test-salt";
        public const uint ClientVersion = 77;
        public const string AppVersion = "1.36.0";
        public const string AppBuild = "1.36.0.1";
        public const string UserA = "EI1111111111111111";
        public const string BotEid = "EI6291940968235008";
        public const string FixedEid = "EI6145601714651136";
        public const string PeriodicalsReferenceEid = "EI5482515761594368";

        private readonly VersionTriple _previous;
        private readonly ServiceProvider _provider;

        public EggIncApiHarness(bool salt = true) {
            _previous = EggIncApi.Versions.Current;
            EggIncApi.SetVersions(ClientVersion, AppVersion, AppBuild);
            InitializeSalt(salt ? Salt : null);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new ImmediateDelayTimeProvider());
            services.AddEggIncApi();
            services.ConfigureAll<HttpClientFactoryOptions>(o => o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = Handler));
            _provider = services.BuildServiceProvider();
            Api = _provider.GetRequiredService<IEggIncApi>();
        }

        public RecordingHandler Handler { get; } = new();

        public IEggIncApi Api { get; }

        public IServiceProvider Services => _provider;

        public RecordedRequest Single() {
            Assert.AreEqual(1, Handler.Requests.Count);
            return Handler.Requests[0];
        }

        public void Dispose() {
            _provider.Dispose();
            EggIncApi.Versions.Set(_previous);
            InitializeSalt(null);
        }

        private static void InitializeSalt(string? salt) {
            var values = new Dictionary<string, string?>();
            if(salt is not null)
                values["ConnectionStrings:ApiSalt"] = salt;
            SecretsHelper.Initialize(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }
    }
}
