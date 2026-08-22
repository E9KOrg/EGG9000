using Discord;
using Ei;
using Google.Protobuf.Reflection;
using Humanizer;
using MessagePack;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using static Ei.GameModifier.Types;

namespace EGG9000.Common.Database.Entities {

    [Table("CustomEggs")]
    public class DBCustomEgg {
        public DBCustomEgg() { }
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
        public DBCustomEgg(CustomEgg customEgg, Emote? emoji) {
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
            ApplyDetails(customEgg);
            GuildEmote = emoji;
            Released = false;
        }

        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Value { get; set; }
        public string _response { get; set; }
        [NotMapped]
        private readonly JsonBlobAccessor<CustomEgg> _details = new();
        [NotMapped]
        public CustomEgg Details => _details.Get(_response);

        public void ApplyDetails(CustomEgg egg) {
            _response = _details.Set(egg, _response);
            Identifier = egg.Identifier;
            Name = egg.Name;
            Description = egg.Description;
            Value = egg.Value;
            Icon = new(egg.Icon);
            Modifiers = [.. egg.Buffs.Select(b => new DBCustomEggModifier(b))];
        }

        public byte[] _iconBytes { get; set; }
        [NotMapped]
        private readonly MessagePackBlobAccessor<DBCustomEggIcon> _icon = new();
        [NotMapped]
        public DBCustomEggIcon Icon {
            get => _icon.Get(_iconBytes);
            set => _iconBytes = _icon.Set(value, _iconBytes);
        }
        public byte[] _modifiersBytes { get; set; }
        [NotMapped]
        private readonly MessagePackBlobAccessor<List<DBCustomEggModifier>> _modifiers = new();
        [NotMapped]
        public List<DBCustomEggModifier> Modifiers {
            get => _modifiers.Get(_modifiersBytes);
            set => _modifiersBytes = _modifiers.Set(value, _modifiersBytes);
        }
        public string EmojiName { get; set; }
        public ulong EmojiId { get; set; }
        [NotMapped]
        public string Emoji {
            get {
                return $"<:{EmojiName}:{EmojiId}>";
            }
        }
        [NotMapped]
        public Emote GuildEmote {
            get {
                if(EmojiId == default || EmojiName == default) return null;
                return new Emote(EmojiId, EmojiName, false);
            }
            set {
                EmojiName = value?.Name ?? "";
                EmojiId = value?.Id ?? ulong.MaxValue;
            }
        }

        public bool Released { get; set; } = false;

        public override bool Equals(object another) {
            if(ReferenceEquals(this, another)) return true;
            if(another is DBCustomEgg dBCustomEgg) {
                if(!dBCustomEgg.Icon.Equals(Icon) || !dBCustomEgg.Value.Equals(Value) || !dBCustomEgg.Identifier.Equals(Identifier)) return false;
                if(Modifiers.Count != dBCustomEgg.Modifiers.Count) return false;
                for(var i = 0; i < Modifiers.Count; i++) if(!dBCustomEgg.Modifiers[i].Equals(Modifiers[i])) return false;
                return true;
            } else if(another is CustomEgg customEgg) return new DBCustomEgg(customEgg, null).Equals(this);
            else return false;
        }

        public override int GetHashCode() {
            return EmojiId.GetHashCode();
        }
    }

    [MessagePackObject]
    public class DBCustomEggIcon {
        public DBCustomEggIcon() { }

        public DBCustomEggIcon(DLCItem dlcItem) {
            Name = dlcItem.Name;
            Directory = dlcItem?.Directory ?? "";
            Extension = dlcItem?.Ext ?? "";
            Compressed = dlcItem?.Compressed ?? false;
            URL = dlcItem?.Url ?? "";
            Checksum = dlcItem?.Checksum ?? "";
        }

        [Key(0)]
        public string Name { get; set; }
        [Key(1)]
        public string Directory { get; set; }
        [Key(2)]
        public string Extension { get; set; }
        [Key(3)]
        public bool Compressed { get; set; }
        [Key(4)]
        public string URL { get; set; }
        [Key(5)]
        public string Checksum { get; set; }

        public override bool Equals(object another) {
            if(ReferenceEquals(this, another)) return true;
            if(another is DBCustomEggIcon icon) return icon.Checksum == Checksum;
            else if(another is DLCItem dlcItem) return new DBCustomEggIcon(dlcItem).Equals(this);
            else return false;
        }

        public override int GetHashCode() {
            return Checksum.GetHashCode();
        }
    }

    [MessagePackObject]
    public class DBCustomEggModifier {
        public DBCustomEggModifier() { }

        public DBCustomEggModifier(GameModifier modifier) {
            Dimension = (int)modifier.Dimension;
            Value = modifier?.Value ?? 1;
            Description = modifier?.Description ?? "";
        }

        [Key(0)]
        public int Dimension { get; set; }
        [Key(1)]
        public double Value { get; set; }
        [Key(2)]
        public string Description { get; set; }

        public GameDimension GetGameDimension() {
            return (GameDimension)Dimension;
        }

        public string GetReadbleGameDimnension() {
            var type = ((GameDimension)Dimension).GetType();
            var name = Enum.GetName(type, Dimension);
            if(name is null) return Dimension.ToString();
            return type.GetField(name)?.GetCustomAttributes(false).OfType<OriginalNameAttribute>().SingleOrDefault()?.Name ?? Dimension.ToString();
        }

        // Title-cased human dimension, e.g. "Egg Value". Shared by the contract-settings embed and the web view.
        public string DimensionName() => GetReadbleGameDimnension().Replace("_", " ").ToLowerInvariant().Titleize();

        // "+" for a buff (value >= 1), "-" for a debuff.
        public string Sign() => Value < 1 ? "-" : "+";

        // Signed percent away from 1.0, e.g. "+15%" / "-5%".
        public string PercentString() {
            var magnitude = Value < 1 ? 1 - Value : Value - 1;
            return $"{Sign()}{(int)(magnitude * 100)}%";
        }

        public override bool Equals(object another) {
            if(ReferenceEquals(this, another)) return true;
            if(another is DBCustomEggModifier modifier) return modifier.Dimension == Dimension && modifier.Value.Equals(Value) && modifier.Description == Description;
            else if(another is GameModifier gameModifier) return new DBCustomEggModifier(gameModifier).Equals(this);
            else return false;
        }

        public override int GetHashCode() {
            return HashCode.Combine(Dimension, Value, Description);
        }
    }
}