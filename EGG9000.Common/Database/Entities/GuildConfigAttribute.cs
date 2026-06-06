using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.Database.Entities {
    public enum GuildConfigKind {
        Bool,
        Int,
        Float,
        String,
        CsvCategories,
        CsvRoles
    }

    /// <summary>
    /// Marks a scalar / comma-list property on <see cref="Guild"/> as user-configurable.
    /// The Discord <c>/a configure</c> command (and, in future, the site) reflects these
    /// instead of hardcoding a field list - so adding a configurable setting is just
    /// annotating the property. The enum-driven parts (channels, roles, coop settings)
    /// are already structure-driven via GuildChannelType / GuildCoopSetting.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class GuildConfigAttribute(string label, string category, GuildConfigKind kind) : Attribute {
        public string Label { get; } = label;
        public string Category { get; } = category;
        public GuildConfigKind Kind { get; } = kind;
        public string Description { get; init; }
    }

    public sealed record GuildConfigField(PropertyInfo Property, GuildConfigAttribute Meta) {
        public string PropName => Property.Name;
        public string Label => Meta.Label;
        public GuildConfigKind Kind => Meta.Kind;
        public string Description => Meta.Description;
    }

    public static class GuildConfigReflection {
        // Reflected once; PropertyInfo set is stable for the process lifetime.
        private static readonly List<GuildConfigField> _fields = [.. typeof(Guild)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p, attr: p.GetCustomAttribute<GuildConfigAttribute>()))
            .Where(x => x.attr is not null)
            .Select(x => new GuildConfigField(x.p, x.attr))];

        public static IReadOnlyList<GuildConfigField> Fields => _fields;

        public static IEnumerable<GuildConfigField> ByKind(params GuildConfigKind[] kinds) =>
            _fields.Where(f => kinds.Contains(f.Kind));

        public static GuildConfigField Get(string propName) =>
            _fields.FirstOrDefault(f => f.PropName == propName);
    }
}
