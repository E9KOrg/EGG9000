//using ProtoBuf;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;

//namespace DiscordCoopCodes.Proto {
//    //[ProtoContract]
//    //public class ContractsProto {
//    //    [ProtoMember(1)]
//    //    public List<ContractProto> Contracts { get; set; }
//    //    [ProtoMember(2)]
//    //    public double U1 { get; set; }
//    //    [ProtoMember(3)]
//    //    public int U2 { get; set; }

//    //    public bool Success { get; set; }
//    //    public string Error { get; set; }
//    //}

//    //[ProtoContract]
//    //public class ContractProto {
//    //    [ProtoMember(1)]
//    //    public string ID { get; set; }
//    //    [ProtoMember(2)]
//    //    public int P2 { get; set; }
//    //    [ProtoMember(3)]
//    //    public List<RewardProto> Rewards { get; set; }
//    //    [ProtoMember(4)]
//    //    public int P4 { get; set; }
//    //    [ProtoMember(5)]
//    //    public int Size { get; set; }
//    //    [ProtoMember(6)]
//    //    public double P6 { get; set; }
//    //    [ProtoMember(7)]
//    //    public double P7 { get; set; }
//    //    [ProtoMember(9)]
//    //    public string Name { get; set; }
//    //    [ProtoMember(10)]
//    //    public string Description { get; set; }
//    //    [ProtoMember(11)]
//    //    public int P11 { get; set; }

//    //    public TimeSpan ContractTime { get { return TimeSpan.FromSeconds(P7); } }
//    //    public DateTimeOffset GoodUntil { get { return DateTimeOffset.FromUnixTimeSeconds((long)P6); } }
//    //    public bool AllowCoop { get { return P4 == 1; } }
//    //}

//    //[ProtoContract]
//    //public class RewardProto {
//    //    [ProtoMember(1)]
//    //    public int P1 { get; set; }
//    //    [ProtoMember(2)]
//    //    public double Target { get; set; }
//    //    public string TargetStr { get { return ArgumentsHelper.NumberToString(Target); } }
//    //    [ProtoMember(3)]
//    //    public int Icon { get; set; }
//    //    [ProtoMember(4)]
//    //    public string Type { get; set; }
//    //    [ProtoMember(5)]
//    //    public double Amount { get; set; }
//    //    [ProtoMember(6)]
//    //    public double P6 { get; set; }

//    //    public string Name {
//    //        get {
//    //            if (Type.Length > 0 && Type != "subtype") {
//    //                return String.Join(" ", Type.Split("_").Select(x => x.First().ToString().ToUpper() + x.Substring(1)));
//    //            }
//    //            switch (Icon) {
//    //                case 2:
//    //                    return "Golden Eggs";
//    //                case 3:
//    //                    return "Soul Eggs";
//    //                case 4:
//    //                    return "Prophecy Eggs";
//    //            }
//    //            return "Unknown";
//    //        }
//    //    }
//    //}

//    [ProtoContract]
//    public class GetPeriodicalsRequest {
//        [ProtoMember(1)]
//        public string user_id { get; set; }
//        [ProtoMember(2)]
//        public int piggy_full { get; set; }
//        [ProtoMember(3)]
//        public int piggy_found_full { get; set; }
//        [ProtoMember(4)]
//        public double seconds_full_realtime { get; set; }
//        [ProtoMember(5)]
//        public double seconds_full_gametime { get; set; }
//        [ProtoMember(8)]
//        public double soul_eggs { get; set; }
//        [ProtoMember(10)]
//        public int current_client_version { get; set; }
//        [ProtoMember(11)]
//        public int debug { get; set; }
//    }

//    [ProtoContract]
//    public class PeriodicalsResponse {
//        //[ProtoMember(1)]
//        //optional ei.SalesInfo sales = 1;
//        //[ProtoMember(2)]
//        //optional ei.EggIncCurrentEvents events = 2;
//        [ProtoMember(3)]
//        public ContractsResponse contracts { get; set; }
//        //optional ei.ContractsResponse contracts = 3;
//        //[ProtoMember(4)]
//        //repeated ei.ServerGift gifts = 4;
//    }

//    [ProtoContract]
//    public class SalesInfo {

//    }

//    [ProtoContract]
//    public class EggIncCurrentEvents {

//    }

//    [ProtoContract]
//    public class ContractsResponse {
//        public bool Success { get; set; }
//        public string Error { get; set; }

//        [ProtoMember(1)]
//        public List<ContractP> contracts { get; set; }
//    }

//    [ProtoContract]
//    public class ContractP {
//        [ProtoMember(1)]
//        public string identifier { get; set; }
//        [ProtoMember(9)]
//        public string name { get; set; }
//        [ProtoMember(10)]
//        public string description { get; set; }
//        [ProtoMember(2)]
//        public Egg egg { get; set; }
//        [ProtoMember(3)]
//        public List<Goal> goals { get; set; }
//        [ProtoMember(4)]
//        public int coop_allowed { get; set; }
//        [ProtoMember(5)]
//        public int max_coop_size { get; set; }
//        [ProtoMember(12)]
//        public int max_boosts { get; set; }
//        [ProtoMember(6)]
//        public double expiration_time { get; set; }
//        [ProtoMember(7)]
//        public double length_seconds { get; set; }
//        [ProtoMember(13)]
//        public double max_soul_eggs { get; set; }
//        [ProtoMember(14)]
//        public int min_client_version { get; set; }
//        [ProtoMember(11)]
//        public int debug { get; set; }

//        public TimeSpan ContractTime { get { return TimeSpan.FromSeconds(length_seconds); } }
//        public DateTimeOffset GoodUntil { get { return DateTimeOffset.FromUnixTimeSeconds((long)expiration_time); } }

//    }

//    [ProtoContract]
//    public class Goal {
//        [ProtoMember(1)]
//        public GoalType type { get; set; }
//        [ProtoMember(2)]
//        public double target_amount { get; set; }
//        [ProtoMember(3)]
//        public RewardType reward_type { get; set; }
//        [ProtoMember(4)]
//        // "subtype" if reward_type is GOLD, SOUL_EGGS, EGGS_OF_PROPHECY, PIGGY_LEVEL_BUMP, etc
//        // EPIC_RESEARCH_ITEM: "transportation_lobbyist", etc
//        // BOOST: "tachyon_prism_purple", "jimbos_purple", "boost_beacon_purple", etc
//        public string reward_sub_type { get; set; }
//        [ProtoMember(5)]
//        public double reward_amount { get; set; }
//    }

//    public enum GoalType {
//        EGGS_LAID = 1,
//        UNKNOWN_GOAL = 100
//    }

//    public enum RewardType {
//        CASH = 1,
//        GOLD = 2,
//        SOUL_EGGS = 3,
//        EGGS_OF_PROPHECY = 4,
//        EPIC_RESEARCH_ITEM = 5,
//        PIGGY_FILL = 6,
//        PIGGY_MULTIPLIER = 7,
//        PIGGY_LEVEL_BUMP = 8,
//        BOOST = 9,
//        UNKNOWN_REWARD = 100
//    }

//    public enum Egg {
//        EDIBLE = 1,
//        SUPERFOOD = 2,
//        MEDICAL = 3,
//        ROCKET_FUEL = 4,
//        SUPER_MATERIAL = 5,
//        FUSION = 6,
//        QUANTUM = 7,
//        IMMORTALITY = 8,
//        TACHYON = 9,
//        GRAVITON = 10,
//        DILITHIUM = 11,
//        PRODIGY = 12,
//        TERRAFORM = 13,
//        ANTIMATTER = 14,
//        DARK_MATTER = 15,
//        AI = 16,
//        NEBULA = 17,
//        UNIVERSE = 18,
//        ENLIGHTENMENT = 19,
//        CHOCOLATE = 100,
//        EASTER = 101,
//        WATERBALLOON = 102,
//        FIREWORK = 103,
//        PUMPKIN = 104,
//        UNKNOWN = 1000
//    }


//    [ProtoContract]
//    public class ServerGift {

//    }
//}
