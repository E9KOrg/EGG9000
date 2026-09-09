using EGG9000.Common.Database.Entities;
using EGG9000.Common.Proto;

using MessagePack;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace EGG9000.Common.Database {
    public static class StorageFingerprint {
        public static string Accounts() => Hash(AccountsDescription());

        public static string CoopStatus() => Hash(CoopStatusDescription());

        public static string AccountsDescription() {
            var lines = new List<string>();
            Walk(typeof(List<EggIncAccount>), [], lines);
            foreach(var type in typeof(Ei.Backup).Assembly.GetTypes()) {
                if(type.GetCustomAttribute<NotStoredAttribute>(false) is { } notStored)
                    lines.Add($"notstored {type.FullName}: {string.Join(",", notStored.Fields.Order(StringComparer.Ordinal))}");
            }
            lines.Sort(StringComparer.Ordinal);
            lines.Add("descriptor " + Hash(Ei.Backup.Descriptor.File.SerializedData.Span));
            lines.Add(Strategy(StorageCompressionStrategy.AccountGraph));
            return string.Join("\n", lines);
        }

        public static string CoopStatusDescription() {
            return string.Join("\n", [
                "descriptor " + Hash(Ei.ContractCoopStatusResponse.Descriptor.File.SerializedData.Span),
                Strategy(StorageCompressionStrategy.CoopStatus)
            ]);
        }

        private static string Strategy(StorageCompressionStrategy strategy) => $"strategy {strategy.Algorithm} zstd={strategy.ZstdLevel} raw={strategy.RawThreshold}";

        private static string Hash(string description) => Hash(Encoding.UTF8.GetBytes(description));

        private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static void Walk(Type type, HashSet<Type> visited, List<string> lines) {
            if(type.IsArray) {
                Walk(type.GetElementType(), visited, lines);
                return;
            }
            if(type.IsGenericType) {
                foreach(var argument in type.GetGenericArguments())
                    Walk(argument, visited, lines);
            }
            if(type.GetCustomAttribute<MessagePackObjectAttribute>() is null || !visited.Add(type))
                return;
            lines.Add($"type {type.FullName}");
            foreach(var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
                var memberType = member switch {
                    PropertyInfo property => property.PropertyType,
                    FieldInfo field => field.FieldType,
                    _ => null
                };
                if(memberType is null)
                    continue;
                var key = member.GetCustomAttribute<KeyAttribute>()?.IntKey;
                var ignored = member.GetCustomAttribute<IgnoreMemberAttribute>() is not null;
                if(key is null && !ignored)
                    continue;
                var gate = member.GetCustomAttribute<DerivedSlotAttribute>()?.Gate ?? "-";
                lines.Add($"member {type.FullName} {key?.ToString() ?? "-"} {member.Name} {Name(memberType)} derived={gate} ignored={ignored}");
                if(!ignored)
                    Walk(memberType, visited, lines);
            }
        }

        private static string Name(Type type) {
            if(type.IsArray)
                return Name(type.GetElementType()) + "[]";
            if(type.IsGenericType)
                return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(Name)) + ">";
            return type.FullName;
        }
    }
}
