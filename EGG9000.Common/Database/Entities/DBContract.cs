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
        public DateTimeOffset GoodUntil { get; set; } //expiration_time
        public string egg { get; set; }
        public int MaxUsers { get; set; }
        public double length_seconds { get; set; }
        public bool cc_only { get; set; } //Subscription needed

        public string _response { get; set; }

        public bool HadTwoRewards { get; set; }

        [NotMapped]
        private Ei.Contract _details { get; set; }
        [NotMapped]
        public Ei.Contract Details {
            get {
                if(_response == null) {
                    return null;
                }
                _details ??= JsonConvert.DeserializeObject<Ei.Contract>(_response);
                return _details;
            }
        }
        public void OverwriteDetails(Ei.Contract details) {
            _details = details;
            _response = JsonConvert.SerializeObject(details);
        }

        public void ApplyDetails(Ei.Contract details) {
            OverwriteDetails(details);
            Name = details.Name;
            GoodUntil = DateTimeOffset.FromUnixTimeSeconds((long)details.ExpirationTime);
            MaxUsers = (int)details.MaxCoopSize;
            egg = details.Egg.ToString();
            cc_only = details.CcOnly;
        }


        public double P7 { get; set; }


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

        // Derived from the proto rather than a DB column so legacy re-runs of old seasonal contracts keep the original season ID
        [NotMapped]
        public string SeasonId => string.IsNullOrEmpty(Details?.SeasonId) ? null : Details.SeasonId;

        public List<GuildContract> GuildContracts { get; set; }

        public DateTimeOffset Created { get; set; }

        public List<Coop> Coops { get; set; }
    }
}
