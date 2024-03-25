//using ProtoBuf;
//using System;
//using System.Collections.Generic;
//using System.Text;

//namespace EGG9000.Bot.Proto {
//    [ProtoContract]
//    public class FirstContactRequestProto {
//        [ProtoMember(1)]
//        public string UserId { get; set; }
//        [ProtoMember(2)]
//        public int P2 { get; set; }
//        [ProtoMember(3)]
//        public int P3 { get; set; }
//    }

//    [ProtoContract]
//    public class FirstContactResponseProto {
//        [ProtoMember(1)]
//        public UserDetailsProto UserDetails { get; set; }

//        //public string DetailsBase64 { get { return System.Convert.ToBase64String(Encoding.ASCII.GetBytes(UserDetails)); } }

//        public bool Success { get; set; }
//        public string Error { get; set; }
//    }

//    [ProtoContract]
//    public class UserDetailsProto {
//        [ProtoMember(1)]
//        public string UserId { get; set; }
//        [ProtoMember(2)]
//        public string UserName { get; set; }
//        [ProtoMember(3)]
//        public double P3 { get; set; }

//        [ProtoMember(4)]
//        public string P4 { get; set; }

//        [ProtoMember(5)]
//        public string P5 { get; set; }

//        [ProtoMember(6)]
//        public string P6 { get; set; }

//        [ProtoMember(7)]
//        public string P7 { get; set; }

//        [ProtoMember(8)]
//        public string P8 { get; set; }

//        [ProtoMember(9)]
//        public string P9 { get; set; }

//        [ProtoMember(10)]
//        public string P10 { get; set; }

//    }
//}
