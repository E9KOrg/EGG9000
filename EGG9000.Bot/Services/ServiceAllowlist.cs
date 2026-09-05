using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Bot.Services {
    public sealed class ServiceAllowlist {
        public const string EnvironmentVariable = "EGG9000_SERVICE_ALLOWLIST";

        private static readonly Lazy<ServiceAllowlist> _default = new(() => Parse(Environment.GetEnvironmentVariable(EnvironmentVariable)));

        private readonly FrozenSet<string> _entries;

        private ServiceAllowlist(FrozenSet<string> entries) {
            _entries = entries;
        }

        public static ServiceAllowlist Default => _default.Value;

        public static ServiceAllowlist Parse(string raw) {
            var entries = (raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
            return new ServiceAllowlist(entries);
        }

        public bool Active => _entries.Count > 0;

        public IReadOnlyCollection<string> Entries => _entries;

        public bool IsEnabled(string typeName) => !Active || _entries.Contains(typeName);

        public bool IsEnabled(Type t) => IsEnabled(t.Name);
    }
}
