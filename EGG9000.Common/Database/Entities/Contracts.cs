using Ei;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace EGG9000.Common.Database.Entities {
    public class Contract {
        public string ID { get; set; }  //identifier
        public string Name { get; set; } //name
        public string Description { get; set; } //description
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

        public string _response { get; set; }

        public bool HadTwoRewards { get; set; }

        public string custom_eggs { get; set; }

        public List<CustomEgg> CustomEggs => JsonConvert.DeserializeObject<List<CustomEgg>>(custom_eggs);

        [NotMapped]
        private Ei.Contract _details { get; set; }
        [NotMapped]
        public Ei.Contract Details {
            get {
                if(_response == null) {
                    return null;
                }
                if(_details == null) {
                    _details = JsonConvert.DeserializeObject<Ei.Contract>(_response);
                }
                return _details;
            }
        }
        public void OverwriteDetails(Ei.Contract details) {
            _details = details;
            _response = JsonConvert.SerializeObject(details);
        }


        public string Rewards { get; set; }
        public int P2 { get; set; }
        public int P4 { get; set; }
        public double P6 { get; set; }
        public double P7 { get; set; }
        public int P11 { get; set; }


        [NotMapped]
        public TimeSpan ContractTime {
            get {
                if (length_seconds == 0) {
                    return TimeSpan.FromSeconds(P7);
                }
                return TimeSpan.FromSeconds(length_seconds);
            }
        }

        [NotMapped]
        public List<Ei.Contract.Types.Goal> GoalsDetail => JsonConvert.DeserializeObject<List<Ei.Contract.Types.Goal>>(goals);

        public List<GuildContract> GuildContracts { get; set; }

        public DateTimeOffset Created { get; set; }

        public List<Coop> Coops { get; set; }
    }
}
