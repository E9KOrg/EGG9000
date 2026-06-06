using Discord;
using Ei;
using Google.Protobuf.Reflection;
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
            Identifier = customEgg.Identifier;
            Name = customEgg.Name;
            Description = customEgg.Description;
            Value = customEgg.Value;
            Icon = new(customEgg.Icon);
            Modifiers = [.. customEgg.Buffs.Select(b => new DBCustomEggModifier(b))];
            GuildEmote = emoji;
            Released = false;
        }

        public string Identifier { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Value { get; set; }
        public byte[] _iconBytes { get; set; }
        [NotMapped]
        private DBCustomEggIcon _icon { get; set; }
        [NotMapped]
        public DBCustomEggIcon Icon {
            get {
                if(_icon != null) return _icon;
                if(_iconBytes == null) return null;
                _icon = MessagePackSerializer.Deserialize<DBCustomEggIcon>(_iconBytes);
                return _icon;
            }
            set {
                _icon = value;
                _iconBytes = MessagePackSerializer.Serialize(value);
            }
        }
        public byte[] _modifiersBytes { get; set; }
        [NotMapped]
        private List<DBCustomEggModifier> _modifiers { get; set; }
        [NotMapped]
        public List<DBCustomEggModifier> Modifiers { 
            get {
                if(_modifiers != null) return _modifiers;
                if(_modifiersBytes == null) return null;
                _modifiers = MessagePackSerializer.Deserialize<List<DBCustomEggModifier>>(_modifiersBytes);
                return _modifiers;
            }
            set {
                _modifiers = value;
                _modifiersBytes = MessagePackSerializer.Serialize(value);
            }
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
    public class  DBCustomEggModifier {
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
            return type.GetField(name).GetCustomAttributes(false).OfType<OriginalNameAttribute>().SingleOrDefault()?.Name ?? Dimension.ToString();
        }

        public override bool Equals(object another) {
            if(ReferenceEquals(this, another)) return true;
            if(another is DBCustomEggModifier modifier) return modifier.GetHashCode() == GetHashCode();
            else if(another is GameModifier gameModifier) return new DBCustomEggModifier(gameModifier).Equals(this);
            else return false;
        }

        public override int GetHashCode() {
            return base.GetHashCode();
        }
    }
}