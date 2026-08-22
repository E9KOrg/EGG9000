using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    public class ExpiringShell {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public Guid id { get; set; }
        public string Name { get; set; }
        public DateTimeOffset Expires { get; set; }
        public uint Price { get; set; }
        public Ei.ShellSpec.Types.AssetType AssetType { get; set; }
        public string Identifier { get; set; }
        public string Json { get; set; }
        public string MessageIds { get; set; }
        public bool Archived { get; set; }

        [NotMapped]
        private readonly JsonBlobAccessor<Ei.ShellObjectSpec> _details = new();
        [NotMapped]
        public Ei.ShellObjectSpec Details => _details.Get(Json);

        public void ApplyDetails(Ei.ShellObjectSpec e) {
            Json = _details.Set(e, Json);
            Identifier = e.Identifier;
            Name = e.Name;
            Expires = DateTimeOffset.UtcNow.AddSeconds(e.SecondsRemaining);
            Price = e.Price;
            AssetType = e.AssetType;
        }

        public ExpiringShell() {
        }

        public ExpiringShell(Ei.ShellObjectSpec e) {
            ApplyDetails(e);
        }
    }
}
