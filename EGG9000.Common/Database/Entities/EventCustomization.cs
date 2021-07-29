using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace DiscordCoopCodes.Database.Entities {
    public class EventCustomization {
        [Key]
        public string Type { get; set; }

        public string Color { get; set; }
        public string Description { get; set; }
        public string Fields { get; set; }
        public string ThumbnailURL { get; set; }
        public string Emoji { get; set; }
        public int Priority { get; set; }

    }

    public class EventField {
        public string Name { get; set; }
        public string Value { get; set; }
    }
}
