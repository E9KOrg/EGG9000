using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EGG9000.ConvertProbe {
    public static class CanonicalJson {
        private const int MaxDepth = 48;
        private static readonly Dictionary<Type, MemberInfo[]> MemberCache = [];

        public static string Serialize(object value) {
            using var stream = new MemoryStream();
            using(var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true })) {
                WriteValue(writer, value, 0);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void WriteValue(Utf8JsonWriter writer, object value, int depth) {
            if(value is null) {
                writer.WriteNullValue();
                return;
            }
            if(depth > MaxDepth) {
                writer.WriteStringValue("<max depth>");
                return;
            }
            switch(value) {
                case string s:
                    writer.WriteStringValue(s);
                    return;
                case bool b:
                    writer.WriteBooleanValue(b);
                    return;
                case Enum e:
                    writer.WriteStringValue(e.ToString());
                    return;
                case double d:
                    WriteDouble(writer, d);
                    return;
                case float f:
                    WriteFloat(writer, f);
                    return;
                case decimal m:
                    writer.WriteRawValue(m.ToString(CultureInfo.InvariantCulture));
                    return;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    writer.WriteRawValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                case char c:
                    writer.WriteStringValue(c.ToString());
                    return;
                case DateTime dateTime:
                    writer.WriteStringValue(dateTime.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case DateTimeOffset dateTimeOffset:
                    writer.WriteStringValue(dateTimeOffset.ToString("O", CultureInfo.InvariantCulture));
                    return;
                case TimeSpan timeSpan:
                    writer.WriteStringValue(timeSpan.ToString("c", CultureInfo.InvariantCulture));
                    return;
                case Guid guid:
                    writer.WriteStringValue(guid.ToString());
                    return;
                case byte[]:
                    writer.WriteNullValue();
                    return;
                case Google.Protobuf.IMessage message:
                    writer.WriteRawValue(Google.Protobuf.JsonFormatter.Default.Format(message), skipInputValidation: true);
                    return;
                case ITuple tuple:
                    WriteTuple(writer, tuple, depth);
                    return;
                case IDictionary dictionary:
                    WriteDictionary(writer, dictionary, depth);
                    return;
                case IEnumerable enumerable:
                    WriteArray(writer, enumerable, depth);
                    return;
            }
            WriteObject(writer, value, depth);
        }

        private static void WriteDouble(Utf8JsonWriter writer, double value) {
            if(double.IsNaN(value) || double.IsInfinity(value)) {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            writer.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteFloat(Utf8JsonWriter writer, float value) {
            if(float.IsNaN(value) || float.IsInfinity(value)) {
                writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            writer.WriteRawValue(value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void WriteTuple(Utf8JsonWriter writer, ITuple tuple, int depth) {
            writer.WriteStartArray();
            for(var i = 0; i < tuple.Length; i++)
                WriteValue(writer, tuple[i], depth + 1);
            writer.WriteEndArray();
        }

        private static void WriteDictionary(Utf8JsonWriter writer, IDictionary dictionary, int depth) {
            var entries = new List<KeyValuePair<string, object>>();
            foreach(DictionaryEntry entry in dictionary)
                entries.Add(new KeyValuePair<string, object>(KeyText(entry.Key), entry.Value));
            writer.WriteStartObject();
            foreach(var entry in entries.OrderBy(x => x.Key, StringComparer.Ordinal)) {
                writer.WritePropertyName(entry.Key);
                WriteValue(writer, entry.Value, depth + 1);
            }
            writer.WriteEndObject();
        }

        private static string KeyText(object key) {
            return key switch {
                null => "null",
                string s => s,
                Enum e => e.ToString(),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => key.ToString()
            };
        }

        private static void WriteArray(Utf8JsonWriter writer, IEnumerable enumerable, int depth) {
            writer.WriteStartArray();
            foreach(var item in enumerable)
                WriteValue(writer, item, depth + 1);
            writer.WriteEndArray();
        }

        private static void WriteObject(Utf8JsonWriter writer, object value, int depth) {
            writer.WriteStartObject();
            foreach(var member in MembersOf(value.GetType())) {
                object memberValue;
                try {
                    memberValue = member is PropertyInfo property ? property.GetValue(value) : ((FieldInfo)member).GetValue(value);
                } catch(Exception e) {
                    var inner = e is TargetInvocationException { InnerException: { } i } ? i : e;
                    writer.WriteString(member.Name, $"<error {inner.GetType().Name}: {inner.Message}>");
                    continue;
                }
                if(memberValue is byte[]) continue;
                writer.WritePropertyName(member.Name);
                WriteValue(writer, memberValue, depth + 1);
            }
            writer.WriteEndObject();
        }

        private static MemberInfo[] MembersOf(Type type) {
            lock(MemberCache) {
                if(MemberCache.TryGetValue(type, out var cached)) return cached;
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p.CanRead
                        && p.GetMethod?.IsPublic == true
                        && p.GetIndexParameters().Length == 0
                        && p.PropertyType != typeof(byte[])
                        && !p.IsDefined(typeof(JsonIgnoreAttribute), true))
                    .Cast<MemberInfo>();
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.FieldType != typeof(byte[]) && !f.IsDefined(typeof(JsonIgnoreAttribute), true))
                    .Cast<MemberInfo>();
                var members = properties.Concat(fields).OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();
                MemberCache[type] = members;
                return members;
            }
        }
    }
}
