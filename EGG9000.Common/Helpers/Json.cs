//using Newtonsoft.Json;
//using Newtonsoft.Json.Serialization;
//using System;
//using System.IO;
//using System.IO.Compression;
//using System.Reflection;

//namespace EGG9000.Common.Helpers {
//    public class IgnoreHasResolver : DefaultContractResolver {
//        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization) {
//            JsonProperty property = base.CreateProperty(member, memberSerialization);
//            if(property.PropertyName.StartsWith("Has")) {
//                property.ShouldSerialize = _ => false;
//            }
//            return property;
//        }
//    }

//    public class JsonHelper {
//        public static void CleanBackup(Ei.Backup Backup) {
//            if(Backup.Contracts == null) {
//                return;
//            }
//            try {
//                foreach(var contract in Backup.Contracts.Archive) {
//                    contract.Contract.Description = "";
//                    contract.Contract.Name = "";
//                }
//                foreach(var contract in Backup.Contracts.Contracts) {
//                    contract.Contract.Description = "";
//                    contract.Contract.Name = "";
//                }
//                //Backup.Contracts.CurrentCoopStatuses.Clear();
//                Backup.Mission = null;
//                Backup.Game.Achievements.Clear();
//                Backup.Game.News.Clear();
//                Backup.Tutorial = null;
//                Backup.Stats.EggTotals.Clear();
//                Backup.Game.MaxFarmSizeReached.Clear();
//                Backup.Game.EggMedalLevel.Clear();
//            } catch(Exception) {

//            }
//        }

//        public static byte[] Zip(byte[] input) {
//            using(var msi = new MemoryStream(input))
//            using(var mso = new MemoryStream()) {
//                using(var gs = new GZipStream(mso, CompressionMode.Compress)) {
//                    //msi.CopyTo(gs);
//                    CopyTo(msi, gs);
//                }

//                return mso.ToArray();
//            }
//        }

//        public static void CopyTo(Stream src, Stream dest) {
//            byte[] bytes = new byte[4096];

//            int cnt;

//            while((cnt = src.Read(bytes, 0, bytes.Length)) != 0) {
//                dest.Write(bytes, 0, cnt);
//            }
//        }
//    }
//}
