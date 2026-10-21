using Discord;
using Discord.Net.Rest;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.Extensions.Caching.Memory;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test {
    internal sealed partial class FakeDiscord : IRestClient {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const ulong BotId = 424_242;

        private readonly HashSet<(ulong Guild, ulong User)> _restMembers = [];
        private readonly HashSet<ulong> _failingMemberGuilds = [];
        private ulong _nextRoleId = 900_000;

        public List<string> Calls { get; } = [];
        public DiscordSocketClient Gateway { get; }
        public DiscordRestClient Rest { get; }

        public FakeDiscord() {
            Gateway = new DiscordSocketClient(new DiscordSocketConfig { RestClientProvider = _ => this });
            Gateway.LoginAsync(TokenType.Bot, "fake", validateToken: false).GetAwaiter().GetResult();
            Rest = new DiscordRestClient(new DiscordRestConfig { RestClientProvider = _ => this });
            Rest.LoginAsync(TokenType.Bot, "fake", validateToken: false).GetAwaiter().GetResult();
            Calls.Clear();
        }

        public IEnumerable<string> Writes => Calls.Where(x => !x.StartsWith("GET "));

        public DiscordHostedService Hosted(params Guild[] dbGuilds) {
            var hosted = (DiscordHostedService)RuntimeHelpers.GetUninitializedObject(typeof(DiscordHostedService));
            var cache = new MemoryCache(new MemoryCacheOptions());
            cache.Set(DiscordHostedService.DBGuildsKey, dbGuilds.ToList());
            Set(hosted, "_gateway", Gateway);
            Set(hosted, "_rest", Rest);
            Set(hosted, "_cache", cache);
            Set(hosted, "_dbGuildsKeySemaphore", new SemaphoreSlim(1, 1));
            return hosted;
        }

        public SocketGuild Guild(ulong id, string name = "guild", ulong ownerId = 0) {
            var guild = Make<SocketGuild>();
            Set(guild, "<Id>k__BackingField", id);
            Set(guild, "<Discord>k__BackingField", Gateway);
            Set(guild, "<Name>k__BackingField", name);
            Set(guild, "<OwnerId>k__BackingField", ownerId);
            Set(guild, "<Features>k__BackingField", typeof(GuildFeatures).GetConstructors(AnyInstance)[0].Invoke([default(GuildFeature), Array.Empty<string>()]));
            Set(guild, "_roles", new ConcurrentDictionary<ulong, SocketRole>());
            Set(guild, "_members", new ConcurrentDictionary<ulong, SocketGuildUser>());
            Set(guild, "_channels", new ConcurrentDictionary<ulong, SocketGuildChannel>());
            Role(guild, id, "@everyone", position: 0);
            var state = typeof(DiscordSocketClient).GetProperty("State", AnyInstance)!.GetValue(Gateway)!;
            state.GetType().GetMethod("AddGuild", AnyInstance)!.Invoke(state, [guild]);
            return guild;
        }

        public SocketRole Role(SocketGuild guild, ulong id, string name, GuildPermissions permissions = default, uint color = 0, int position = 1) {
            var role = Make<SocketRole>();
            Set(role, "<Id>k__BackingField", id);
            Set(role, "<Discord>k__BackingField", Gateway);
            Set(role, "<Name>k__BackingField", name);
            Set(role, "<Position>k__BackingField", position);
            Set(role, "<Permissions>k__BackingField", permissions);
            Set(role, "<Colors>k__BackingField", RoleColors.Solid(new Color(color)));
            Set(role, "<Guild>k__BackingField", guild);
            Get<ConcurrentDictionary<ulong, SocketRole>>(guild, "_roles")[id] = role;
            return role;
        }

        public SocketGuildUser User(SocketGuild guild, ulong id, ulong[]? roles = null, string? nickname = null, bool isBot = false, bool restKnown = true) {
            var global = RuntimeHelpers.GetUninitializedObject(typeof(SocketGuild).Assembly.GetType("Discord.WebSocket.SocketGlobalUser", throwOnError: true)!);
            Set(global, "<Id>k__BackingField", id);
            Set(global, "<Discord>k__BackingField", Gateway);
            Set(global, "<Username>k__BackingField", $"user{id}");
            Set(global, "<IsBot>k__BackingField", isBot);

            var user = Make<SocketGuildUser>();
            Set(user, "<Id>k__BackingField", id);
            Set(user, "<Discord>k__BackingField", Gateway);
            Set(user, "<GlobalUser>k__BackingField", global);
            Set(user, "<Guild>k__BackingField", guild);
            Set(user, "<Nickname>k__BackingField", nickname);
            ImmutableArray<ulong> roleIds = [guild.Id, .. roles ?? Array.Empty<ulong>()];
            Set(user, "_roleIds", roleIds);
            Get<ConcurrentDictionary<ulong, SocketGuildUser>>(guild, "_members")[id] = user;
            if(restKnown)
                _restMembers.Add((guild.Id, id));
            return user;
        }

        public SocketCategoryChannel Category(SocketGuild guild, ulong id, string name, params Overwrite[] overwrites) {
            var category = Make<SocketCategoryChannel>();
            Set(category, "<Id>k__BackingField", id);
            Set(category, "<Discord>k__BackingField", Gateway);
            Set(category, "<Guild>k__BackingField", guild);
            Set(category, "<Name>k__BackingField", name);
            Set(category, "<ChannelType>k__BackingField", ChannelType.Category);
            ImmutableArray<Overwrite> overwriteList = [.. overwrites];
            Set(category, "_overwrites", overwriteList);
            Get<ConcurrentDictionary<ulong, SocketGuildChannel>>(guild, "_channels")[id] = category;
            return category;
        }

        public void RestMember(ulong guildId, ulong userId) => _restMembers.Add((guildId, userId));

        public void FailMemberLookups(ulong guildId) => _failingMemberGuilds.Add(guildId);

        public static void SetMemberCounts(SocketGuild guild, int memberCount, int downloadedMemberCount) {
            Set(guild, "<MemberCount>k__BackingField", memberCount);
            Set(guild, "<DownloadedMemberCount>k__BackingField", downloadedMemberCount);
        }

        public static T Make<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        public static void Set(object target, string field, object? value) => Field(target.GetType(), field).SetValue(target, value);

        private static T Get<T>(object target, string field) => (T)Field(target.GetType(), field).GetValue(target)!;

        private static FieldInfo Field(Type type, string name) {
            for(var t = type; t is not null; t = t.BaseType) {
                var field = t.GetField(name, AnyInstance | BindingFlags.DeclaredOnly);
                if(field is not null)
                    return field;
            }
            throw new MissingFieldException(type.FullName, name);
        }

        private (HttpStatusCode, string) Handle(string method, string endpoint) {
            if(endpoint.StartsWith("oauth2/applications/@me"))
                return (HttpStatusCode.OK, $$"""{"id":"{{BotId}}","name":"bot","description":"","flags":0,"bot_public":true,"bot_require_code_grant":false}""");
            if(endpoint.StartsWith("users/@me"))
                return (HttpStatusCode.OK, $$"""{"id":"{{BotId}}","username":"bot","discriminator":"0"}""");

            var member = MemberEndpoint().Match(endpoint);
            if(method == "GET" && member.Success) {
                var guildId = ulong.Parse(member.Groups[1].Value);
                var userId = ulong.Parse(member.Groups[2].Value);
                if(_failingMemberGuilds.Contains(guildId))
                    return (HttpStatusCode.Forbidden, """{"message":"Missing Access","code":50001}""");
                return _restMembers.Contains((guildId, userId))
                    ? (HttpStatusCode.OK, $$"""{"user":{"id":"{{userId}}","username":"u","discriminator":"0"},"roles":[],"joined_at":"2020-01-01T00:00:00+00:00","deaf":false,"mute":false}""")
                    : (HttpStatusCode.NotFound, """{"message":"Unknown Member","code":10007}""");
            }

            var guild = GuildEndpoint().Match(endpoint);
            if(method == "GET" && guild.Success)
                return (HttpStatusCode.OK, $$"""{"id":"{{guild.Groups[1].Value}}","name":"g","owner_id":"{{BotId}}","preferred_locale":"en-US","roles":[{"id":"{{guild.Groups[1].Value}}","name":"@everyone","permissions":"0","position":0,"color":0}],"emojis":[],"features":[],"stickers":[]}""");

            if(method == "POST" && endpoint.EndsWith("/roles"))
                return (HttpStatusCode.OK, $$"""{"id":"{{_nextRoleId++}}","name":"created","permissions":"0","position":1,"color":0}""");

            return method is "PUT" or "DELETE"
                ? (HttpStatusCode.NoContent, string.Empty)
                : (HttpStatusCode.OK, "{}");
        }

        private Task<RestResponse> Respond(string method, string endpoint, string? json) {
            Calls.Add(json is null ? $"{method} {endpoint}" : $"{method} {endpoint} {json}");
            var (code, body) = Handle(method, endpoint);
            return Task.FromResult(new RestResponse(code, new Dictionary<string, string>(), new MemoryStream(Encoding.UTF8.GetBytes(body))));
        }

        public void SetHeader(string key, string value) {
        }

        public void SetCancelToken(CancellationToken cancelToken) {
        }

        public Task<RestResponse> SendAsync(string method, string endpoint, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
            Respond(method, endpoint, null);

        public Task<RestResponse> SendAsync(string method, string endpoint, string json, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
            Respond(method, endpoint, json);

        public Task<RestResponse> SendAsync(string method, string endpoint, IReadOnlyDictionary<string, object> multipartParams, CancellationToken cancelToken, bool headerOnly = false, string? reason = null, IEnumerable<KeyValuePair<string, IEnumerable<string>>>? requestHeaders = null) =>
            Respond(method, endpoint, null);

        public void Dispose() {
        }

        [GeneratedRegex(@"^guilds/(\d+)/members/(\d+)$")]
        private static partial Regex MemberEndpoint();

        [GeneratedRegex(@"^guilds/(\d+)\?")]
        private static partial Regex GuildEndpoint();
    }
}
