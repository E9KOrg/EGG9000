using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    [Table("Events")]
    public class DBEvent {
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

        public string _response { get; set; }
        [NotMapped]
        private readonly JsonBlobAccessor<Ei.EggIncEvent> _details = new();
        [NotMapped]
        public Ei.EggIncEvent Details => _details.Get(_response);

        public bool DetailsChanged(Ei.EggIncEvent e) {
            if(Details is not { } stored)
                return true;
            var incoming = e.Clone();
            incoming.SecondsRemaining = stored.SecondsRemaining;
            return !stored.Equals(incoming);
        }

        public void ApplyDetails(Ei.EggIncEvent e) {
            _response = _details.Set(e, _response);
            Identifier = e.Identifier;
            Type = e.Type;
            Multiplier = e.Multiplier;
            Subtitle = e.Subtitle;
            CcOnly = e.CcOnly;
        }

        public DBEvent() {
        }

        public DBEvent(Ei.EggIncEvent e) {
            ApplyDetails(e);
            Ends = DateTimeOffset.UtcNow.AddSeconds(e.SecondsRemaining);
        }

        public bool SignficantlyDifferent(Ei.EggIncEvent e) {
            if(e is null || this is null) return true;
            return Type != e.Type || Multiplier != e.Multiplier;
        }
    }
}
