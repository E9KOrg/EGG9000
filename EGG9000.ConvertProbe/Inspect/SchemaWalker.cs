using MessagePack;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EGG9000.ConvertProbe.Inspect {
    public sealed record Finding(int AccountIndex, string Path, string Member, string Kind, string Declared, string MsgpackType, string RawValue) {
        public bool IsProblem => Kind is not (FindingKind.ExtraSlot or FindingKind.RetiredSlot);
    }

    public static class FindingKind {
        public const string Overflow = "overflow";
        public const string TypeMismatch = "type-mismatch";
        public const string NilInValueSlot = "nil-in-value-slot";
        public const string NonFinite = "non-finite";
        public const string BadLength = "bad-length";
        public const string ExtraSlot = "extra-slot";
        public const string RetiredSlot = "retired-slot";
        public const string WalkError = "walk-error";
        public const string SchemaSkipped = "schema-skipped";
    }

    public sealed class SchemaWalker(SlotShape accountShape) {
        private readonly SlotShape _accountShape = accountShape;
        private List<Finding> _findings;
        private int _account;
        private string _currentPath;

        public List<Finding> Walk(byte[] plain) {
            _findings = [];
            _account = -1;
            _currentPath = "";
            try {
                var reader = new MessagePackReader(plain);
                if(reader.TryReadNil()) return _findings;
                if(reader.NextMessagePackType != MessagePackType.Array) {
                    Add(FindingKind.TypeMismatch, "", "List<EggIncAccount>", "List<EggIncAccount>", ref reader);
                    return _findings;
                }
                var count = reader.ReadArrayHeader();
                for(var i = 0; i < count; i++) {
                    _account = i;
                    WalkValue(ref reader, _accountShape, "", "EggIncAccount");
                }
                if(!reader.End)
                    _findings.Add(new Finding(-1, "", "", FindingKind.BadLength, "end of blob", "", $"{plain.Length - reader.Consumed} trailing bytes"));
            } catch(Exception e) {
                _findings.Add(new Finding(_account, _currentPath, "", FindingKind.WalkError, "", "", e.GetType().Name + ": " + e.Message));
            }
            return _findings;
        }

        private void WalkValue(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            _currentPath = path;
            var type = reader.NextMessagePackType;
            if(type == MessagePackType.Nil) {
                reader.ReadNil();
                if(!shape.AllowNil)
                    _findings.Add(new Finding(_account, path, member, FindingKind.NilInValueSlot, shape.Declared, "nil", "nil"));
                return;
            }
            switch(shape.Kind) {
                case SlotKind.Integer:
                    WalkInteger(ref reader, shape, path, member);
                    break;
                case SlotKind.Float:
                    if(type == MessagePackType.Float) {
                        var value = reader.ReadDouble();
                        if(double.IsNaN(value) || double.IsInfinity(value))
                            _findings.Add(new Finding(_account, path, member, FindingKind.NonFinite, shape.Declared, "float", value.ToString(CultureInfo.InvariantCulture)));
                    } else if(type == MessagePackType.Integer) {
                        reader.Skip();
                    } else {
                        Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                    }
                    break;
                case SlotKind.Bool:
                    Expect(ref reader, shape, path, member, type == MessagePackType.Boolean);
                    break;
                case SlotKind.String:
                case SlotKind.Binary:
                    Expect(ref reader, shape, path, member, type is MessagePackType.String or MessagePackType.Binary);
                    break;
                case SlotKind.DateTime:
                    Expect(ref reader, shape, path, member, IsTimestamp(reader));
                    break;
                case SlotKind.DateTimeOffset:
                    WalkDateTimeOffset(ref reader, shape, path, member);
                    break;
                case SlotKind.Array:
                    WalkArray(ref reader, shape, path, member);
                    break;
                case SlotKind.Map:
                    WalkMap(ref reader, shape, path, member);
                    break;
                case SlotKind.Tuple:
                    WalkTuple(ref reader, shape, path, member);
                    break;
                case SlotKind.Object:
                    WalkObject(ref reader, shape, path);
                    break;
                default:
                    _findings.Add(new Finding(_account, path, member, FindingKind.SchemaSkipped, shape.Declared, Describe(reader), shape.Note ?? ""));
                    reader.Skip();
                    break;
            }
        }

        private void WalkInteger(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            if(reader.NextMessagePackType != MessagePackType.Integer) {
                Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                return;
            }
            var code = reader.NextCode;
            var format = MessagePackCode.ToFormatName(code);
            if(code == MessagePackCode.UInt64) {
                var unsigned = reader.ReadUInt64();
                if(unsigned > shape.Max)
                    _findings.Add(new Finding(_account, path, member, FindingKind.Overflow, shape.Declared, format, unsigned.ToString(CultureInfo.InvariantCulture)));
                return;
            }
            var signed = reader.ReadInt64();
            if(signed < shape.Min || (signed >= 0 && (ulong)signed > shape.Max))
                _findings.Add(new Finding(_account, path, member, FindingKind.Overflow, shape.Declared, format, signed.ToString(CultureInfo.InvariantCulture)));
        }

        private void WalkDateTimeOffset(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            if(reader.NextMessagePackType != MessagePackType.Array) {
                Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                return;
            }
            var count = reader.ReadArrayHeader();
            if(count != 2)
                _findings.Add(new Finding(_account, path, member, FindingKind.BadLength, shape.Declared, "array", $"array[{count}], expected [DateTime, short]"));
            for(var i = 0; i < count; i++) {
                if(i == 0) Expect(ref reader, shape, path + ".DateTime", member, IsTimestamp(reader));
                else if(i == 1) WalkInteger(ref reader, ShortShape, path + ".OffsetMinutes", member);
                else reader.Skip();
            }
        }

        private void WalkArray(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            if(reader.NextMessagePackType != MessagePackType.Array) {
                Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                return;
            }
            var count = reader.ReadArrayHeader();
            for(var i = 0; i < count; i++)
                WalkValue(ref reader, shape.Element, $"{path}[{i}]", member);
        }

        private void WalkMap(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            if(reader.NextMessagePackType != MessagePackType.Map) {
                Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                return;
            }
            var count = reader.ReadMapHeader();
            for(var i = 0; i < count; i++) {
                WalkValue(ref reader, shape.Key, $"{path}[#{i}].key", member);
                WalkValue(ref reader, shape.Value, $"{path}[#{i}].value", member);
            }
        }

        private void WalkTuple(ref MessagePackReader reader, SlotShape shape, string path, string member) {
            if(reader.NextMessagePackType != MessagePackType.Array) {
                Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
                return;
            }
            var count = reader.ReadArrayHeader();
            if(count != shape.Items.Length)
                _findings.Add(new Finding(_account, path, member, FindingKind.BadLength, shape.Declared, "array", $"array[{count}], expected {shape.Items.Length} items"));
            for(var i = 0; i < count; i++) {
                if(i < shape.Items.Length) WalkValue(ref reader, shape.Items[i], $"{path}.Item{i + 1}", member);
                else reader.Skip();
            }
        }

        private void WalkObject(ref MessagePackReader reader, SlotShape shape, string path) {
            var schema = shape.Object;
            if(schema.Skipped is not null) {
                _findings.Add(new Finding(_account, path, schema.Name, FindingKind.SchemaSkipped, shape.Declared, Describe(reader), schema.Skipped));
                reader.Skip();
                return;
            }
            if(reader.NextMessagePackType != MessagePackType.Array) {
                Add(FindingKind.TypeMismatch, path, schema.Name, shape.Declared, ref reader);
                return;
            }
            var count = reader.ReadArrayHeader();
            var prefix = path.Length == 0 ? "" : path + ".";
            for(var key = 0; key < count; key++) {
                if(schema.Members.TryGetValue(key, out var slot)) {
                    WalkValue(ref reader, slot.Shape, prefix + slot.Name, $"{schema.Name}.{slot.Name} (key {key})");
                    continue;
                }
                if(reader.NextMessagePackType == MessagePackType.Nil) {
                    reader.ReadNil();
                    continue;
                }
                var kind = key > schema.MaxKey ? FindingKind.ExtraSlot : FindingKind.RetiredSlot;
                Add(kind, prefix + "key" + key, $"{schema.Name} (key {key})", "none", ref reader);
            }
        }

        private void Expect(ref MessagePackReader reader, SlotShape shape, string path, string member, bool matches) {
            if(matches) {
                reader.Skip();
                return;
            }
            Add(FindingKind.TypeMismatch, path, member, shape.Declared, ref reader);
        }

        private void Add(string kind, string path, string member, string declared, ref MessagePackReader reader) {
            var code = reader.NextCode;
            var msgpackType = $"{MessagePackCode.ToFormatName(code)} (0x{code:X2})";
            _findings.Add(new Finding(_account, path, member, kind, declared, msgpackType, Describe(reader)));
            reader.Skip();
        }

        private static bool IsTimestamp(MessagePackReader reader) {
            if(reader.NextMessagePackType != MessagePackType.Extension) return false;
            var peek = reader.CreatePeekReader();
            return peek.ReadExtensionFormatHeader().TypeCode == ReservedMessagePackExtensionTypeCode.DateTime;
        }

        private static readonly SlotShape ShortShape = new SlotSchemaBuilder().Build(typeof(short));

        private static string Describe(MessagePackReader reader) {
            var peek = reader.CreatePeekReader();
            try {
                switch(peek.NextMessagePackType) {
                    case MessagePackType.Nil:
                        return "nil";
                    case MessagePackType.Boolean:
                        return peek.ReadBoolean() ? "true" : "false";
                    case MessagePackType.Integer:
                        return peek.NextCode == MessagePackCode.UInt64
                            ? peek.ReadUInt64().ToString(CultureInfo.InvariantCulture)
                            : peek.ReadInt64().ToString(CultureInfo.InvariantCulture);
                    case MessagePackType.Float:
                        return peek.ReadDouble().ToString("R", CultureInfo.InvariantCulture);
                    case MessagePackType.String:
                        return "\"" + Markdown.Clip(peek.ReadString(), 48) + "\"";
                    case MessagePackType.Binary:
                        return $"bin[{peek.ReadBytes()?.Length ?? 0}]";
                    case MessagePackType.Array:
                        return $"array[{peek.ReadArrayHeader()}]";
                    case MessagePackType.Map:
                        return $"map[{peek.ReadMapHeader()}]";
                    case MessagePackType.Extension:
                        var header = peek.ReadExtensionFormatHeader();
                        return $"ext(type {header.TypeCode}, {header.Length} bytes)";
                    default:
                        return $"code 0x{peek.NextCode:X2}";
                }
            } catch(Exception e) {
                return "unreadable: " + e.GetType().Name;
            }
        }
    }
}
