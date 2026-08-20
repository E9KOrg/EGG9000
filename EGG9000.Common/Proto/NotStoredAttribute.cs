using Google.Protobuf;
using Google.Protobuf.Reflection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EGG9000.Common.Proto {
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class NotStoredAttribute(params string[] fields) : Attribute {
        public string[] Fields { get; } = fields;
    }

    public static class StorageTrimmer {
        private sealed class TrimPlan {
            public FieldDescriptor[] Cleared { get; init; }
            public FieldDescriptor[] Children { get; init; }
        }

        private static readonly ConcurrentDictionary<Type, TrimPlan> Plans = new();

        public static byte[] TrimmedBytes<T>(T message) where T : IMessage<T>, IDeepCloneable<T> {
            ArgumentNullException.ThrowIfNull(message);
            var clone = message.Clone();
            Apply(clone);
            return clone.ToByteArray();
        }

        private static void Apply(IMessage message) {
            var descriptor = message.Descriptor;
            var plan = Plans.GetOrAdd(descriptor.ClrType, _ => BuildPlan(descriptor));

            foreach(var field in plan.Cleared)
                field.Accessor.Clear(message);

            foreach(var field in plan.Children) {
                if(field.Accessor.GetValue(message) is IMessage child)
                    Apply(child);
            }
        }

        private static TrimPlan BuildPlan(MessageDescriptor descriptor) {
            var fields = descriptor.Fields.InDeclarationOrder();
            var attribute = descriptor.ClrType.GetCustomAttribute<NotStoredAttribute>(false);

            var cleared = new List<FieldDescriptor>();
            if(attribute is not null) {
                var byProperty = fields.ToDictionary(x => x.PropertyName);
                var unknown = new List<string>();
                foreach(var name in attribute.Fields) {
                    if(byProperty.TryGetValue(name, out var field))
                        cleared.Add(field);
                    else
                        unknown.Add(name);
                }
                if(unknown.Count > 0)
                    throw new ArgumentException($"[NotStored] on {descriptor.ClrType.FullName} names fields that do not exist: {string.Join(", ", unknown)}");
            }

            var clearedSet = cleared.ToHashSet();
            FieldDescriptor[] children = [.. fields.Where(x => x.FieldType == FieldType.Message && !x.IsRepeated && !x.IsMap && !clearedSet.Contains(x))];

            return new TrimPlan { Cleared = [.. cleared], Children = children };
        }
    }
}
