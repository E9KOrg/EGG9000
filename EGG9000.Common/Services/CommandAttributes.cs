using Discord.WebSocket;

using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace EGG9000.Common.Commands {

    public enum StaffOnlyLevel {
        None = 0,
        ChickenTender = 1,
        FarmHand = 2,
        CluckingCoordinator = 3,
        Admin = 4
    };

    [AttributeUsage(AttributeTargets.Method)]
    public class UserCommandAttribute : System.Attribute {
        public string Name = "";
        public StaffOnlyLevel AdminOnly;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class SlashCommandAttribute : System.Attribute {
        public string Description = "";
        public StaffOnlyLevel AdminOnly = StaffOnlyLevel.None;
        public string ParentCommand { get; set; } = "";
        public bool AllowInDMs;
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ComponentCommandAttribute : System.Attribute {
    }
    [AttributeUsage(AttributeTargets.Method)]
    public class ModalAttribute : System.Attribute {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class SlashParamAttribute : System.Attribute {
        public string Description = "";
        public bool Required = true;
        public Type AutocompleteHandler;
        public bool PositiveOnly = false;
        public int StringMaxLength = 6000;
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public class ComponentDataAttribute : System.Attribute {
    }

    public class SlashCommandFunction : CommandFunctionBase {
        public SlashCommandAttribute Details { get; set; }
        public List<SlashCommandFunction> SubFunctions { get; set; }
    }
    public class UserCommandFunction : CommandFunctionBase {
        public UserCommandAttribute Details { get; set; }
    }
    public class ComponentCommandFunction : CommandFunctionBase {
        public ComponentCommandAttribute Details { get; set; }
    }
    public class ModalCommandFunction : CommandFunctionBase {
        public ModalAttribute Details { get; set; }
    }


    public class CommandFunctionBase {
        public MethodInfo MethodInfo { get; set; }
        public ParameterInfo[] Parameters { get; set; }
        public string Name { get; set; }
    }


    public class ButtonObject {

    }

    public interface IAutoCompleteHandler {
        public Task Run(SocketAutocompleteInteraction arg, List<Guild> guilds);
    }

    /// <summary>
    /// Implemented by autocomplete handlers that serve options from a cache. Lets the
    /// "Missing Option (Refresh)" choice (value <see cref="AutoCompleteRefresh.Sentinel"/>)
    /// force a repopulation of the backing cache, rate-limited centrally.
    /// </summary>
    public interface IRefreshableAutoComplete {
        /// <summary>Stable name of the backing cache; used as the rate-limit key.</summary>
        string CacheName { get; }
        Task RefreshAsync();
    }

    /// <summary>Shared sentinel + per-cache rate limit for the autocomplete "force refresh" option.</summary>
    public static class AutoCompleteRefresh {
        public const string Sentinel = "__cache_refresh__";
        public const string Label = "Missing Option (Refresh)";
        public static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(30);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _last = new();

        /// <summary>Records a refresh and returns true if allowed; false if one happened within <see cref="MinInterval"/>.</summary>
        public static bool TryMarkRefresh(string cacheName) {
            var now = DateTimeOffset.Now;
            var prev = _last.TryGetValue(cacheName, out var t) ? t : DateTimeOffset.MinValue;
            if(now - prev < MinInterval)
                return false;
            _last[cacheName] = now;
            return true;
        }

        /// <summary>Seconds until the next refresh is allowed for this cache (0 if allowed now).</summary>
        public static int SecondsUntilAllowed(string cacheName) {
            if(!_last.TryGetValue(cacheName, out var t))
                return 0;
            var remaining = MinInterval - (DateTimeOffset.Now - t);
            return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
        }
    }
}
