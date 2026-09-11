using MessagePack;

namespace EGG9000.Common.Database {
    [MessagePackObject]
    public class CustomResearch {
        [Key(0)]
        public string Id { get; set; }
        [Key(1)]
        public uint Level { get; set; }

        public CustomResearch() { }
        public CustomResearch(Ei.Backup.Types.ResearchItem item) {
            Id = item.Id;
            Level = item.Level;
        }
    }
}
