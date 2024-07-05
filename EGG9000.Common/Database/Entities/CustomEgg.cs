using Discord;
using Ei;
using MessagePack;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using static Ei.GameModifier.Types;

namespace EGG9000.Common.Database.Entities {

    [Table("CustomEggs")]
    public class DBCustomEgg {
        [Key(0)]
        public string Identifier { get; set; }
        [Key(1)]
        public string Name { get; set; }
        [Key(2)]
        public string Description { get; set; }
        [Key(3)]
        public double Value { get; set; }
        [Key(4)]
        public DBCustomEggIcon Icon { get; set; }
        [Key(5)]
        public List<DBCustomEggModifier> Modifiers { get; set; }
        [Key(6)]
        public string EmojiName { get; set; }
        [Key(7)]
        public ulong EmojiId { get; set; }

        public DBCustomEgg(CustomEgg customEgg, GuildEmote emoji) {
            Identifier = customEgg.Identifier;
            Name = customEgg.Name;
            Description = customEgg.Description;
            Value = customEgg.Value;
            Icon = new(customEgg.Icon);
            Modifiers = customEgg.Buffs.Select(b => new DBCustomEggModifier(b)).ToList();

            EmojiName = emoji.Name;
            EmojiId = emoji.Id;
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
    }
}