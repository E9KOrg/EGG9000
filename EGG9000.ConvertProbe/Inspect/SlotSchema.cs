using MessagePack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EGG9000.ConvertProbe.Inspect {
    public enum SlotKind {
        Object,
        Integer,
        Float,
        Bool,
        String,
        Binary,
        DateTime,
        DateTimeOffset,
        Array,
        Map,
        Tuple,
        Unsupported
    }

    public sealed class SlotShape {
        public SlotKind Kind { get; init; }
        public Type Clr { get; init; }
        public bool AllowNil { get; init; }
        public long Min { get; init; }
        public ulong Max { get; init; }
        public SlotShape Element { get; init; }
        public SlotShape Key { get; init; }
        public SlotShape Value { get; init; }
        public SlotShape[] Items { get; init; }
        public ObjectSchema Object { get; init; }
        public string Note { get; init; }

        public string Declared => TypeName(Clr);

        public static string TypeName(Type type) {
            if(type is null) return "?";
            if(Nullable.GetUnderlyingType(type) is { } inner) return TypeName(inner) + "?";
            if(type.IsArray) return TypeName(type.GetElementType()) + "[]";
            if(type.IsGenericType) {
                var name = type.Name;
                var tick = name.IndexOf('`');
                if(tick >= 0) name = name[..tick];
                if(type.FullName?.StartsWith("System.ValueTuple", StringComparison.Ordinal) == true)
                    return "(" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ")";
                return name + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
            }
            return Type.GetTypeCode(type) switch {
                TypeCode.Byte when !type.IsEnum => "byte",
                TypeCode.SByte when !type.IsEnum => "sbyte",
                TypeCode.Int16 when !type.IsEnum => "short",
                TypeCode.UInt16 when !type.IsEnum => "ushort",
                TypeCode.Int32 when !type.IsEnum => "int",
                TypeCode.UInt32 when !type.IsEnum => "uint",
                TypeCode.Int64 when !type.IsEnum => "long",
                TypeCode.UInt64 when !type.IsEnum => "ulong",
                TypeCode.Single => "float",
                TypeCode.Double => "double",
                TypeCode.Boolean => "bool",
                TypeCode.String => "string",
                _ => type.IsEnum ? type.Name + ":" + TypeName(Enum.GetUnderlyingType(type)) : type.Name
            };
        }
    }

    public sealed record SlotMember(int Key, string Name, SlotShape Shape);

    public sealed class ObjectSchema {
        public Type Type { get; init; }
        public Dictionary<int, SlotMember> Members { get; } = [];
        public int MaxKey { get; set; } = -1;
        public string Skipped { get; set; }
        public string Name => Type.Name;
    }

    public sealed class SlotSchemaBuilder {
        private readonly Dictionary<Type, SlotShape> _objects = [];
        private readonly List<string> _notes = [];

        public IReadOnlyList<string> Notes => _notes;
        public List<ObjectSchema> ObjectSchemas() => [.. _objects.Values.Select(v => v.Object)];

        public SlotShape Build(Type type) {
            var underlying = Nullable.GetUnderlyingType(type);
            if(underlying is not null) {
                var inner = Build(underlying);
                return new SlotShape {
                    Kind = inner.Kind, Clr = type, AllowNil = true, Min = inner.Min, Max = inner.Max,
                    Element = inner.Element, Key = inner.Key, Value = inner.Value, Items = inner.Items, Object = inner.Object, Note = inner.Note
                };
            }
            if(type.IsEnum)
                return Integer(type, Enum.GetUnderlyingType(type));
            if(type == typeof(string)) return new SlotShape { Kind = SlotKind.String, Clr = type, AllowNil = true };
            if(type == typeof(byte[])) return new SlotShape { Kind = SlotKind.Binary, Clr = type, AllowNil = true };
            if(type == typeof(bool)) return new SlotShape { Kind = SlotKind.Bool, Clr = type };
            if(type == typeof(float) || type == typeof(double)) return new SlotShape { Kind = SlotKind.Float, Clr = type };
            if(type == typeof(DateTime)) return new SlotShape { Kind = SlotKind.DateTime, Clr = type };
            if(type == typeof(DateTimeOffset)) return new SlotShape { Kind = SlotKind.DateTimeOffset, Clr = type };
            if(IsInteger(type)) return Integer(type, type);
            if(type.IsArray && type.GetArrayRank() == 1)
                return new SlotShape { Kind = SlotKind.Array, Clr = type, AllowNil = true, Element = Build(type.GetElementType()) };
            if(type.IsGenericType) {
                var definition = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();
                if(definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>) || definition == typeof(SortedDictionary<,>))
                    return new SlotShape { Kind = SlotKind.Map, Clr = type, AllowNil = true, Key = Build(args[0]), Value = Build(args[1]) };
                if(definition == typeof(KeyValuePair<,>))
                    return new SlotShape { Kind = SlotKind.Tuple, Clr = type, Items = [Build(args[0]), Build(args[1])] };
                if(typeof(ITuple).IsAssignableFrom(type))
                    return new SlotShape { Kind = SlotKind.Tuple, Clr = type, AllowNil = !type.IsValueType, Items = [.. args.Select(Build)] };
                if(definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IEnumerable<>) || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(HashSet<>))
                    return new SlotShape { Kind = SlotKind.Array, Clr = type, AllowNil = true, Element = Build(args[0]) };
            }
            if(type.GetCustomAttribute<MessagePackObjectAttribute>() is { } attribute)
                return BuildObject(type, attribute);
            var note = $"{SlotShape.TypeName(type)}: no schema mapping, contents skipped";
            _notes.Add(note);
            return new SlotShape { Kind = SlotKind.Unsupported, Clr = type, AllowNil = !type.IsValueType, Note = note };
        }

        private SlotShape BuildObject(Type type, MessagePackObjectAttribute attribute) {
            if(_objects.TryGetValue(type, out var existing)) return existing;
            var schema = new ObjectSchema { Type = type };
            var shape = new SlotShape { Kind = SlotKind.Object, Clr = type, AllowNil = true, Object = schema };
            _objects[type] = shape;
            if(attribute.KeyAsPropertyName) {
                schema.Skipped = "keyAsPropertyName (string keys)";
                _notes.Add($"{type.Name}: {schema.Skipped}, contents skipped");
                return shape;
            }
            foreach(var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
                if(member is not PropertyInfo and not FieldInfo) continue;
                if(member.GetCustomAttribute<IgnoreMemberAttribute>() is not null) continue;
                var key = member.GetCustomAttribute<KeyAttribute>();
                if(key is null) continue;
                if(key.IntKey is not { } index) {
                    schema.Skipped = $"string key on {member.Name}";
                    _notes.Add($"{type.Name}: {schema.Skipped}, contents skipped");
                    return shape;
                }
                var memberType = member is PropertyInfo p ? p.PropertyType : ((FieldInfo)member).FieldType;
                if(schema.Members.ContainsKey(index)) {
                    _notes.Add($"{type.Name}: duplicate key {index} ({schema.Members[index].Name}, {member.Name})");
                    continue;
                }
                schema.Members[index] = new SlotMember(index, member.Name, Build(memberType));
                schema.MaxKey = Math.Max(schema.MaxKey, index);
            }
            return shape;
        }

        private static bool IsInteger(Type type) => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;

        private static SlotShape Integer(Type declared, Type underlying) {
            (long min, ulong max) = Type.GetTypeCode(underlying) switch {
                TypeCode.Byte => (0L, (ulong)byte.MaxValue),
                TypeCode.SByte => ((long)sbyte.MinValue, (ulong)sbyte.MaxValue),
                TypeCode.Int16 => ((long)short.MinValue, (ulong)short.MaxValue),
                TypeCode.UInt16 => (0L, (ulong)ushort.MaxValue),
                TypeCode.Int32 => ((long)int.MinValue, (ulong)int.MaxValue),
                TypeCode.UInt32 => (0L, (ulong)uint.MaxValue),
                TypeCode.Int64 => (long.MinValue, (ulong)long.MaxValue),
                TypeCode.UInt64 => (0L, ulong.MaxValue),
                _ => throw new InvalidOperationException($"{underlying} is not an integer type.")
            };
            return new SlotShape { Kind = SlotKind.Integer, Clr = declared, Min = min, Max = max };
        }
    }
}
