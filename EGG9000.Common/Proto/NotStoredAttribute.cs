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

            foreach(var field in fields)
                GuardRepeatedElement(descriptor, field, clearedSet);

            FieldDescriptor[] children = [.. fields.Where(x => x.FieldType == FieldType.Message && !x.IsRepeated && !x.IsMap && !clearedSet.Contains(x))];

            return new TrimPlan { Cleared = [.. cleared], Children = children };
        }

        private static void GuardRepeatedElement(MessageDescriptor descriptor, FieldDescriptor field, HashSet<FieldDescriptor> clearedSet) {
            if(field.FieldType != FieldType.Message || (!field.IsRepeated && !field.IsMap) || clearedSet.Contains(field))
                return;
            var elementField = field.IsMap ? field.MessageType.Fields.InFieldNumberOrder()[1] : field;
            if(elementField.FieldType != FieldType.Message)
                return;
            var element = elementField.MessageType;
            if(element.ClrType?.GetCustomAttribute<NotStoredAttribute>(false) is not null)
                throw new InvalidOperationException($"{descriptor.ClrType.FullName}.{field.PropertyName} is a repeated or map field whose element type {element.ClrType.FullName} carries [NotStored]; StorageTrimmer cannot trim inside repeated or map fields.");
        }
    }
}
