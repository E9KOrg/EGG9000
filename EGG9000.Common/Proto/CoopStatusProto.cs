//using EGG9000.Common.Database.Entities;
//using Newtonsoft.Json;
//using ProtoBuf;
//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Text;

//namespace EGG9000.Bot.Proto {
//    [ProtoContract]
//    public class CoopRequestProto {
//        [ProtoMember(1)]
//        public string ContractName { get; set; }
//        [ProtoMember(2)]
//        public string CoopName { get; set; }
//    }

//    [ProtoContract]
//    public class CoopStatusProto {
//        [ProtoMember(1)]
//        public string ContractName { get; set; }
//        [ProtoMember(2)]
//        public double Total { get; set; }
//        public string TotalString { get { return ArgumentsHelper.NumberToString(Total); } }
//        [ProtoMember(3)]
//        public string CoopName { get; set; }
//        [ProtoMember(4)]
//        public List<byte[]> ParticipantProto { get; set; }
//        [ProtoMember(5)]
//        public double TimeLeftSeconds { get; set; }
//        [ProtoMember(6)]
//        public int P6 { get; set; }
//        [ProtoMember(7)]
//        public double Rate { get; set; }
//        public string RatePerHour { get { return ArgumentsHelper.NumberToString(Rate * 60 * 60) + "/HR"; } }

//        private List<CoopParticipantProto> _participants { get; set; }
//        [JsonIgnore]
//        public List<CoopParticipantProto> Participants {
//            get {
//                if (_participants != null)
//                    return _participants;
//                _participants = new List<CoopParticipantProto>();
//                if (ParticipantProto == null)
//                    return _participants;
//                foreach (var p in ParticipantProto) {
//                    var base64 = Convert.ToBase64String(p);
//                    var ms = new MemoryStream();
//                    ms.Write(p);
//                    ms.Position = 0;
//                    var participant = Serializer.Deserialize<Proto.CoopParticipantProto>(ms);
//                    participant.TimeLeftSeconds = TimeLeftSeconds;
//                    _participants.Add(participant);
//                }

//                return _participants;
//            }
//        }

//        public double Projected {
//            get {
//                return Participants.Sum(x => x.Projected);
//            }
//        }

//        //public bool ProjectedToFinish(Contract contract, long guildId) {
//        //    var targetAmount = contract.Details.GoalLevels.Count > 0 ? contract.Details.GoalLevels[guildId == 689271292842737732 ? 1 : 0].Goals.Last().TargetAmount : contract.Details.Goals.Last().TargetAmount;
//        //    //var target = contract.GoalsDetail.Last().TargetAmount;
//        //    return Projected > targetAmount;
//        //}
//        //public bool Finished(Contract contract, long guildId) {
//        //    var targetAmount = contract.Details.GoalLevels.Count > 0 ? contract.Details.GoalLevels[guildId == 689271292842737732 ? 1 : 0].Goals.Last().TargetAmount : contract.Details.Goals.Last().TargetAmount;
//        //    //var target = contract.GoalsDetail.Last().TargetAmount;
//        //    return Total > targetAmount;
//        //}


//        public bool Success { get; set; }
//        public string Error { get; set; }
//    }

//    [ProtoContract]
//    public class CoopParticipantProto {
//        [ProtoMember(1)]
//        public string P1 { get; set; }
//        [ProtoMember(2)]
//        public string Name { get; set; }
//        [ProtoMember(3)]
//        public double Total { get; set; }
//        public string TotalString { get { return ArgumentsHelper.NumberToString(Total); } }
//        [ProtoMember(4)]
//        public int P4 { get; set; }
//        [ProtoMember(5)]
//        public int P5 { get; set; }
//        [ProtoMember(6)]
//        public double Rate { get; set; }

//        public double TimeLeftSeconds { get; set; }

//        public TimeSpan LastActive { get; set; }
//        public string DiscordName { get; set; }

//        public string Id { get { return P1; } }
//        public string RateString { get { return ArgumentsHelper.NumberToString(Rate) + "/s"; } }
//        public bool Sleeping { get { return P4 == 0; } }
//        public double Projected {
//            get {
//                if (TimeLeftSeconds <= 0) {
//                    return Total;
//                }
//                return Total + Rate * TimeLeftSeconds + Rate * LastActive.TotalSeconds;
//            }
//        }
//    }
//}
