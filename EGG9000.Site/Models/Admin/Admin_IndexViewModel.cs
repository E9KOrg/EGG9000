using System;
using System.Collections.Generic;

namespace EGG9000.Site.Models.Admin {
    public class Admin_IndexViewModel {
        public List<DBContract> Contracts { get; set; }
        public List<Admin_GuildDetails> Guilds { get; set; }
        public Dictionary<DateTimeOffset, int[]> Days { get; set; }
        public List<DBContract> ContractsToScore { get; set; }
        public Guild Guild { get; set; }
        public int CoopsWithoutThreads { get; set; }
    }

    public class Admin_GuildDetails {
        public string Name { get; set; }
        public int ThreadCount { get; set; }
        public int ActiveCoops { get; set; }
        public int FinishedCoops { get; set; }
    }
}
