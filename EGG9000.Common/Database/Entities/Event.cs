using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Text;

namespace DiscordCoopCodes.Database.Entities {
    public class Event {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid id { get; set; }
        public string Identifier { get; set; }
        public DateTimeOffset Ends { get; set; }
        public string Type { get; set; }
        public double Multiplier { get; set; }
        public string Subtitle { get; set; }
        public string MessageIds { get; set; }
        public bool Ended { get; set; }

        public Event() {
        }

        public Event(Ei.EggIncEvent e) {
            Identifier = e.Identifier;
            Type = e.Type;
            Multiplier = e.Multiplier;
            Subtitle = e.Subtitle;
            Ends = DateTimeOffset.Now.AddSeconds(e.SecondsRemaining);
                
        }
    }
}
