//using EGG9000.Common.Database.Entities;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace EGG9000.Bot.Helpers {
//    public class Charts {
//        public static String GetUrlForCoop(List<CoopStatus> statuses) {


//            var statusClean = statuses.SelectMany(x => x.Status.Participants.Select(y => new {
//                y.Total, y.P1, y.Name, x.Created
//            })).GroupBy(x => x.P1);

//            var participants = new List<ParticipantDetails>();
//            var start = statuses.Min(x => x.Created);
//            var p1s = statusClean.Select(x => x.Last().P1);
//            var n = String.Join("|", p1s);
//            foreach (var participant in statusClean.Where(x => x.Last().P1 != "null")) {
//                double previous = 0;
//                var p = new ParticipantDetails { Name = participant.Last().Name, Statues = new List<ParticipantStatus>() };
//                participants.Add(p);
//                foreach(var status in participant) {
//                    if(status.Total != previous) {
//                        previous = status.Total;
//                        if(status.Total > UInt64.MaxValue) {
//                            throw new Exception("Value Too Large");
//                        }
//                        p.Statues.Add(new ParticipantStatus { Total = (UInt64)status.Total, TimeSinceStart = (UInt64)(status.Created - start).TotalSeconds });
//                    }
//                }
//            }

//            UInt64 tss = 60 * 60 * 24 * 2;
//            var dataString = String.Join("|",participants.Select(x =>
//                String.Join(",", x.Statues.Where(y => y.TimeSinceStart < tss).Select(y => y.TimeSinceStart)) + "|" + String.Join(",", x.Statues.Where(y => y.TimeSinceStart < tss).Select(y => y.Total))
//            ));

//            var names = String.Join("|", participants.Select(x => x.Name));

//            var colors = "641E16,512E5F,154360,0E6251,145A32,7D6608,7842127B7D7D,4D5656,1B2631,78281F,4A235A,1B4F72,0B5345,186A3B,7E5109,6E2C00,626567,424949,17202A";


//            var url = $"https://image-charts.com/chart?cht=lxy&chs=800x600&chd=a:{dataString}&chdl={names}&chco={colors}";

//            return url;
//        }

//        public class ParticipantDetails {
//            public string Name { get; set; }
//            public List<ParticipantStatus> Statues { get; set; }
//        }

//        public class ParticipantStatus {
//            public UInt64 TimeSinceStart { get; set; }
//            public UInt64 Total { get; set; }
//        }
//    }
//}
