using MessagePack;

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EGG9000.Common.Helpers {
    public sealed class ObjectSizeNode {
        public required string Name { get; init; }
        public required long SizeBytes { get; init; }
        public List<ObjectSizeNode> Children { get; init; } = [];
    }

    public static class SizeTreeHelper {
        private static readonly MessagePackSerializerOptions SizeOptions =
            MessagePackSerializerOptions.Standard.WithCompression(MessagePackCompression.Lz4BlockArray);

        public static ObjectSizeNode BuildCustomBackupSizeTree(object backup) {
            var root = BuildObjectSizeTree(backup, "root", new HashSet<object>(ReferenceEqualityComparer.Instance));
            SortTree(root);
            return root;
        }

        public static string BuildCustomBackupSizeTreeJson(object backup, bool indented = true) {
            var tree = BuildCustomBackupSizeTree(backup);
            return ToJson(tree, indented);
        }

        public static string ToJson(ObjectSizeNode node, bool indented = true) {
            var options = new JsonSerializerOptions { WriteIndented = indented };
            return JsonSerializer.Serialize(node, options);
        }

        private static ObjectSizeNode BuildObjectSizeTree(object value, string name, HashSet<object> visited) {
            if(value is null) {
                return new ObjectSizeNode { Name = name, SizeBytes = 0 };
            }

            var type = value.GetType();

            if(!type.IsValueType && !visited.Add(value)) {
                return new ObjectSizeNode { Name = $"{name} (ref)", SizeBytes = 0 };
            }

            var node = new ObjectSizeNode {
                Name = $"{name} ({type.Name})",
                SizeBytes = GetSerializedSize(value, type)
            };

            if(IsLeaf(type)) {
                return node;
            }

            if(value is IDictionary dictionary) {
                var i = 0;
                foreach(DictionaryEntry entry in dictionary) {
                    var entryNode = new ObjectSizeNode {
                        Name = $"Entry[{i++}]",
                        SizeBytes = 0,
                        Children = [
                            BuildObjectSizeTree(entry.Key, "Key", visited),
                            BuildObjectSizeTree(entry.Value, "Value", visited)
                        ]
                    };
                    node.Children.Add(entryNode);
                }

                return node;
            }

            if(value is IEnumerable enumerable && value is not string) {
                var items = enumerable.Cast<object>().ToList();
                node.Children.AddRange(BuildAggregatedCollectionChildren(items, visited));
                return node;
            }

            node.Children.AddRange(BuildAggregatedMembers([value], visited));
            return node;
        }

        private static List<ObjectSizeNode> BuildAggregatedCollectionChildren(List<object> items, HashSet<object> visited) {
            if(items.Count == 0) {
                return [];
            }

            var nodes = new List<ObjectSizeNode>();

            foreach(var group in items.GroupBy(x => x?.GetType())) {
                var groupType = group.Key;
                var groupItems = group.Where(x => x is not null).ToList();
                var count = group.Count();

                var totalSize = groupType is null
                    ? 0
                    : groupItems.Sum(item => GetSerializedSize(item, groupType));

                var groupNode = new ObjectSizeNode {
                    Name = $"{groupType?.Name ?? "null"} x{count}",
                    SizeBytes = totalSize
                };

                if(groupType is not null && !IsLeaf(groupType)) {
                    var filtered = groupItems.Where(item => TryAddVisited(item, visited)).ToList();
                    if(filtered.Count > 0) {
                        groupNode.Children.AddRange(BuildAggregatedMembers(filtered, visited));
                    }
                }

                nodes.Add(groupNode);
            }

            return nodes;
        }

        private static List<ObjectSizeNode> BuildAggregatedMembers(IReadOnlyList<object> items, HashSet<object> visited) {
            if(items.Count == 0) {
                return [];
            }

            var type = items[0].GetType();
            var nodes = new List<ObjectSizeNode>();

            foreach(var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if(!property.CanRead || property.GetIndexParameters().Length > 0) {
                    continue;
                }

                if(HasIgnoredAttribute(property)) {
                    continue;
                }

                var values = new List<object>();
                foreach(var item in items) {
                    try {
                        values.Add(property.GetValue(item));
                    } catch {
                        // ignored
                    }
                }

                nodes.Add(BuildAggregatedNodeFromValues(property.Name, values, visited));
            }

            foreach(var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                if(HasIgnoredAttribute(field)) {
                    continue;
                }

                var values = new List<object>();
                foreach(var item in items) {
                    try {
                        values.Add(field.GetValue(item));
                    } catch {
                        // ignored
                    }
                }

                nodes.Add(BuildAggregatedNodeFromValues(field.Name, values, visited));
            }

            return nodes;
        }

        private static ObjectSizeNode BuildAggregatedNodeFromValues(string name, List<object> values, HashSet<object> visited) {
            if(values.Count == 0) {
                return new ObjectSizeNode { Name = name, SizeBytes = 0 };
            }

            var nonNull = values.Where(v => v is not null).ToList();
            var totalSize = nonNull.Sum(v => GetSerializedSize(v, v.GetType()));

            var node = new ObjectSizeNode {
                Name = name,
                SizeBytes = totalSize
            };

            if(nonNull.Count == 0) {
                return node;
            }

            var groups = nonNull.GroupBy(v => v.GetType()).ToList();

            if(groups.Count > 1) {
                foreach(var group in groups) {
                    var groupType = group.Key;
                    var groupItems = group.ToList();

                    var groupNode = new ObjectSizeNode {
                        Name = $"{groupType.Name} x{groupItems.Count}",
                        SizeBytes = groupItems.Sum(v => GetSerializedSize(v, groupType))
                    };

                    if(!IsLeaf(groupType)) {
                        var filtered = groupItems.Where(item => TryAddVisited(item, visited)).ToList();
                        if(filtered.Count > 0) {
                            if(typeof(IEnumerable).IsAssignableFrom(groupType) && groupType != typeof(string)) {
                                var allItems = FlattenEnumerableItems(filtered);
                                groupNode.Children.AddRange(BuildAggregatedCollectionChildren(allItems, visited));
                            } else {
                                groupNode.Children.AddRange(BuildAggregatedMembers(filtered, visited));
                            }
                        }
                    }

                    node.Children.Add(groupNode);
                }

                return node;
            }

            var singleType = groups[0].Key;
            if(IsLeaf(singleType)) {
                return node;
            }

            var singleItems = groups[0].Where(item => TryAddVisited(item, visited)).ToList();
            if(singleItems.Count == 0) {
                return node;
            }

            if(typeof(IEnumerable).IsAssignableFrom(singleType) && singleType != typeof(string)) {
                var allItems = FlattenEnumerableItems(singleItems);
                node.Children.AddRange(BuildAggregatedCollectionChildren(allItems, visited));
                return node;
            }

            node.Children.AddRange(BuildAggregatedMembers(singleItems, visited));
            return node;
        }

        private static List<object> FlattenEnumerableItems(IEnumerable<object> values) {
            var items = new List<object>();
            foreach(var value in values) {
                if(value is IEnumerable enumerable) {
                    items.AddRange(enumerable.Cast<object>());
                }
            }
            return items;
        }

        private static bool TryAddVisited(object value, HashSet<object> visited) {
            if(value is null) {
                return false;
            }

            var type = value.GetType();
            return type.IsValueType || visited.Add(value);
        }

        private static void SortTree(ObjectSizeNode node) {
            if(node.Children.Count == 0) {
                return;
            }

            foreach(var child in node.Children) {
                SortTree(child);
            }

            node.Children.Sort(static (a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        }

        private static long GetSerializedSize(object value, Type type) {
            try {
                var bytes = MessagePackSerializer.Serialize(type, value, SizeOptions);
                return bytes.LongLength;
            } catch {
                return 0;
            }
        }

        private static bool HasIgnoredAttribute(MemberInfo member) {
            return Attribute.IsDefined(member, typeof(IgnoreMemberAttribute)) ||
                   Attribute.IsDefined(member, typeof(NotMappedAttribute));
        }

        private static bool IsLeaf(Type type) {
            if(type.IsPrimitive || type.IsEnum) return true;
            if(type == typeof(string)) return true;
            if(type == typeof(decimal) ||
               type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) ||
               type == typeof(Guid) ||
               type == typeof(TimeSpan)) return true;

            return false;
        }
    }
}