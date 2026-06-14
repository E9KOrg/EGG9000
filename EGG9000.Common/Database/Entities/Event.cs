using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
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
        public bool CcOnly { get; set; } = false;

        public Event() {
        }

        public Event(Ei.EggIncEvent e) {
            Identifier = e.Identifier;
            Type = e.Type;
            Multiplier = e.Multiplier;
            Subtitle = e.Subtitle;
            Ends = DateTimeOffset.UtcNow.AddSeconds(e.SecondsRemaining);
            CcOnly = e.CcOnly;
        }

        public bool SignficantlyDifferent(Ei.EggIncEvent e) {
            if(e is null || this is null) return true;
            return Type != e.Type || Multiplier != e.Multiplier;
        }
    }
}
