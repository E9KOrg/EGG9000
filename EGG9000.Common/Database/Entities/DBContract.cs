using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    // TODO: Rename table to DBContract
    [Table("Contracts")]
    public class DBContract {
        public string ID { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public DateTimeOffset GoodUntil { get; set; } //expiration_time
        public string egg { get; set; }
        public string goals { get; set; }
        public bool coop_allowed { get; set; }
        public int MaxUsers { get; set; }
        public int max_boosts { get; set; }
        public double max_soul_eggs { get; set; }
        public int min_client_version { get; set; }
        public bool debug { get; set; }
        public double length_seconds { get; set; }
        public bool cc_only { get; set; } //Subscription needed
        public double egg_value { get; set; }
        public string Rewards { get; set; }

        public string _response { get; set; }

        public bool HadTwoRewards { get; set; }

        [NotMapped]
        private readonly JsonBlobAccessor<Ei.Contract> _details = new();
        [NotMapped]
        public Ei.Contract Details => _details.Get(_response);
        public void OverwriteDetails(Ei.Contract details) {
            _response = _details.Set(details, _response);
        }

        public void ApplyDetails(Ei.Contract details) {
            OverwriteDetails(details);
            Name = details.Name;
            Description = details.Description;
            goals = JsonConvert.SerializeObject(details.Goals);
            GoodUntil = DateTimeOffset.FromUnixTimeSeconds((long)details.ExpirationTime);
            MaxUsers = (int)details.MaxCoopSize;
            coop_allowed = details.CoopAllowed;
            max_boosts = (int)details.MaxBoosts;
            max_soul_eggs = details.MaxSoulEggs;
            min_client_version = (int)details.MinClientVersion;
            debug = details.Debug;
            length_seconds = details.LengthSeconds;
            egg = details.Egg.ToString();
            cc_only = details.CcOnly;
        }


        public int P2 { get; set; }
        public int P4 { get; set; }
        public double P6 { get; set; }
        public double P7 { get; set; }
        public int P11 { get; set; }


        [NotMapped]
        public TimeSpan ContractTime {
            get {
                var fromDetails = Details?.LengthSeconds ?? 0;
                if(fromDetails > 0) {
                    return TimeSpan.FromSeconds(fromDetails);
                }
                if(length_seconds > 0) {
                    return TimeSpan.FromSeconds(length_seconds);
                }
                return TimeSpan.FromSeconds(P7);
            }
        }

        [NotMapped]
        public List<Ei.Contract.Types.Goal> GoalsDetail => JsonConvert.DeserializeObject<List<Ei.Contract.Types.Goal>>(goals);

        // Derived from the proto rather than a DB column so legacy re-runs of old seasonal contracts keep the original season ID
        [NotMapped]
        public string SeasonId => string.IsNullOrEmpty(Details?.SeasonId) ? null : Details.SeasonId;

        public List<GuildContract> GuildContracts { get; set; }

        public DateTimeOffset Created { get; set; }

        public List<Coop> Coops { get; set; }
    }
}
