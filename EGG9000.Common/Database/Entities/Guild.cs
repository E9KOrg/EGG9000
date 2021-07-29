
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;

namespace DiscordCoopCodes.Database.Entities {
    public class Guild {
        public ulong Id { get; set; }
        public string Name { get; set; }
        public string ActiveElites { get; set; }
        public string InactiveElites { get; set; }
        public string ActiveStandards { get; set; }
        public string InactiveStandards { get; set; }

        public ulong DiscordSeverId { get; set; }
        public string OverflowServersJson { get; set; }
        [NotMapped]
        public ReadOnlyCollection<ulong> OverflowServers {
            get {
                return JsonConvert.DeserializeObject<ReadOnlyCollection<ulong>>(OverflowServersJson ?? "[]");
            }
        }

        public ulong? DemeritLogChannel { get; set; }
    }
}
