//using EGG9000.Bot.Proto;
//using EGG9000.Common.Helpers;
//using Newtonsoft.Json;
//using System;
//using System.Collections.Generic;
//using System.ComponentModel.DataAnnotations.Schema;
//using System.IO;
//using System.IO.Compression;
//using System.Text;

//namespace EGG9000.Common.Database.Entities {
//    public class CoopStatus {
//        public Guid Id { get; set; }
//        public Guid CoopId { get; set; }
//        public DateTimeOffset Created { get; set; }

//        public Coop Coop { get; set; }

//        public byte[] _StatusCompressed { get; set; }

//        [NotMapped]
//        private Ei.ContractCoopStatusResponse _status { get; set; }

//        [NotMapped]
//        public Ei.ContractCoopStatusResponse Status {
//            get {
//                if (_status != null)
//                    return _status;
//                if (_StatusCompressed == null)
//                    return null;
//                using (var msi = new MemoryStream(_StatusCompressed))
//                using (var mso = new MemoryStream()) {
//                    using (var gs = new GZipStream(msi, CompressionMode.Decompress)) {
//                        //gs.CopyTo(mso);
//                        CopyTo(gs, mso);
//                    }

//                    _status = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse>(Encoding.UTF8.GetString(mso.ToArray()));
//                    return _status;
//                }
//            }
//            set {
//                _status = value;
//                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, new JsonSerializerSettings { ContractResolver = new CustomContractResolver() }));

//                using (var msi = new MemoryStream(bytes))
//                using (var mso = new MemoryStream()) {
//                    using (var gs = new GZipStream(mso, CompressionMode.Compress)) {
//                        //msi.CopyTo(gs);
//                        CopyTo(msi, gs);
//                    }

//                    _StatusCompressed = mso.ToArray();
//                }
//            }
//        }

//        public static void CopyTo(Stream src, Stream dest) {
//            byte[] bytes = new byte[4096];

//            int cnt;

//            while ((cnt = src.Read(bytes, 0, bytes.Length)) != 0) {
//                dest.Write(bytes, 0, cnt);
//            }
//        }
//    }
//}
