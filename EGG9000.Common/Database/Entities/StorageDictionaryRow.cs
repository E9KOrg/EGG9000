using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    public class StorageDictionaryRow {
        public const string AccountsCorpus = "accounts";
        public const string CoopStatusCorpus = "coopstatus";

        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int Id { get; set; }
        [Required]
        public string Corpus { get; set; }
        [Required]
        public string Fingerprint { get; set; }
        public DateTimeOffset TrainedAt { get; set; }
        public DateTimeOffset EvaluatedAt { get; set; }
        public int SampleCount { get; set; }
        public long HoldoutBytesPlain { get; set; }
        public long HoldoutBytesActive { get; set; }
        public long HoldoutBytesCandidate { get; set; }
        [Required]
        public byte[] Bytes { get; set; }
        public bool Active { get; set; }
    }
}
