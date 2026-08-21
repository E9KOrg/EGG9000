using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace EGG9000.Common.Database {
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class DerivedSlotAttribute(string gate) : Attribute {
        public string Gate { get; } = gate;
    }

    public sealed class DerivedSlotFormatter<T>(int initialBufferSize = 4096) : IMessagePackFormatter<T> where T : class {
        private readonly int _initialBufferSize = initialBufferSize;

        private enum SlotDefault {
            Nil,
            Zero,
            False
        }

        private sealed class DerivedSlot {
            public int GateIndex { get; init; }
            public SlotDefault Default { get; init; }
        }

        private static readonly IMessagePackFormatter<T> Inner = StandardResolver.Instance.GetFormatterWithVerify<T>();
        private static readonly Func<T, byte[]>[] Gates;
        private static readonly DerivedSlot[] Plan;

        static DerivedSlotFormatter() {
            (Gates, Plan) = BuildPlan();
        }

        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options) {
            if(value is null) {
                writer.WriteNil();
                return;
            }

            var present = new bool[Gates.Length];
            var anyPresent = false;
            for(var g = 0; g < Gates.Length; g++) {
                present[g] = Gates[g](value) is { Length: > 0 };
                anyPresent |= present[g];
            }

            var buffer = new ArrayBufferWriter<byte>(_initialBufferSize);
            var inner = writer.Clone(buffer);
            Inner.Serialize(ref inner, value, options);
            inner.Flush();

            if(Plan.Length == 0) {
                writer.WriteRaw(buffer.WrittenSpan);
                return;
            }

            var reader = new MessagePackReader(buffer.WrittenMemory);
            if(!anyPresent || reader.NextMessagePackType != MessagePackType.Array) {
                writer.WriteRaw(buffer.WrittenSpan);
                return;
            }

            var count = reader.ReadArrayHeader();
            writer.WriteArrayHeader(count);
            for(var i = 0; i < count; i++) {
                if(i < Plan.Length && Plan[i] is { } slot && present[slot.GateIndex]) {
                    reader.Skip();
                    WriteDefault(ref writer, slot.Default);
                    continue;
                }
                writer.WriteRaw(reader.ReadRaw());
            }
        }

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
            return Inner.Deserialize(ref reader, options);
        }

        private static void WriteDefault(ref MessagePackWriter writer, SlotDefault slotDefault) {
            switch(slotDefault) {
                case SlotDefault.False:
                    writer.Write(false);
                    break;
                case SlotDefault.Zero:
                    writer.Write(0);
                    break;
                default:
                    writer.WriteNil();
                    break;
            }
        }

        private static (Func<T, byte[]>[] Gates, DerivedSlot[] Plan) BuildPlan() {
            var gates = new List<string>();
            var found = new List<(int Index, DerivedSlot Slot)>();
            foreach(var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if(property.GetCustomAttribute<DerivedSlotAttribute>() is not { } derived)
                    continue;
                if(property.GetCustomAttribute<KeyAttribute>()?.IntKey is not { } index)
                    throw new InvalidOperationException($"[DerivedSlot] on {typeof(T).FullName}.{property.Name} requires an int [Key].");
                var gateIndex = gates.IndexOf(derived.Gate);
                if(gateIndex < 0) {
                    gates.Add(derived.Gate);
                    gateIndex = gates.Count - 1;
                }
                found.Add((index, new DerivedSlot { GateIndex = gateIndex, Default = Classify(property) }));
            }

            if(found.Count == 0)
                return ([], []);

            var plan = new DerivedSlot[found.Max(x => x.Index) + 1];
            foreach(var (index, slot) in found)
                plan[index] = slot;
            return ([.. gates.Select(BuildGate)], plan);
        }

        private static Func<T, byte[]> BuildGate(string name) {
            var gate = typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if(gate is null || gate.PropertyType != typeof(byte[]))
                throw new InvalidOperationException($"[DerivedSlot] gate '{name}' on {typeof(T).FullName} must name a public byte[] property.");
            var parameter = Expression.Parameter(typeof(T), "value");
            return Expression.Lambda<Func<T, byte[]>>(Expression.Property(parameter, gate), parameter).Compile();
        }

        private static SlotDefault Classify(PropertyInfo property) {
            var type = property.PropertyType;
            if(type == typeof(bool))
                return SlotDefault.False;
            if(type.IsEnum)
                return SlotDefault.Zero;
            if(!type.IsValueType || Nullable.GetUnderlyingType(type) is not null)
                return SlotDefault.Nil;
            return Type.GetTypeCode(type) switch {
                TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                    or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double => SlotDefault.Zero,
                _ => throw new InvalidOperationException($"[DerivedSlot] on {typeof(T).FullName}.{property.Name} has no suppressed default for {type.FullName}.")
            };
        }
    }

    public static class StorageMessagePack {
        public static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
            .WithCompression(MessagePackCompression.Lz4BlockArray)
            .WithResolver(CompositeResolver.Create(
                new IMessagePackFormatter[] {
                    new DerivedSlotFormatter<CustomBackup>(65536),
                    new DerivedSlotFormatter<CustomFarm>()
                },
                new IFormatterResolver[] { StandardResolver.Instance }));
    }
}
