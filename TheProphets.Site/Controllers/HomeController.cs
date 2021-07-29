using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ProtoBuf;
using TheProphets.Site.Models;

namespace TheProphets.Site.Controllers {
    public class HomeController : Controller {
        public IActionResult Index() {
            return View();
        }

        public IActionResult Privacy() {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public const int MaxFileLength = 1024 * 1024;
        [Route("/decode")]
        public ActionResult Decode(string hex = null, string base64 = null, IFormFile file = null, bool deep = true) {
            byte[] data = null;
            try {
                if (hex != null) hex = hex.Trim();
                if (base64 != null) base64 = base64.Trim();

                if (file != null && file.Length <= MaxFileLength) {
                    using (var stream = file.OpenReadStream())
                    using (var ms = new MemoryStream((int)file.Length)) {
                        stream.CopyTo(ms);
                        data = ms.ToArray();
                    }
                } else if (!string.IsNullOrWhiteSpace(hex)) {
                    hex = hex.Replace(" ", "").Replace("-", "");
                    int len = hex.Length / 2;
                    var tmp = new byte[len];
                    for (int i = 0; i < len; i++) {
                        tmp[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
                    }
                    data = tmp;
                } else if (!string.IsNullOrWhiteSpace(base64)) {
                    data = Convert.FromBase64String(base64);
                }
            } catch { }
            return View(new DecodeModel(data, deep));
        }

        public class DecodeModel {
            private ArraySegment<byte> data;
            public bool Deep { get; }

            public int SkipField { get; }

            private DecodeModel(byte[] data, bool deep, int offset, int count, int skipField = 0) {
                this.data = data == null
                    ? default
                    : new ArraySegment<byte>(data, offset, count);
                Deep = deep;
                SkipField = skipField;
            }
            public DecodeModel(byte[] data, bool deep) : this(data, deep, 0, data?.Length ?? 0) { }

            public string AsHex() => ContainsValue ? BitConverter.ToString(data.Array, data.Offset, data.Count) : null;

            public string AsHex(int offset, int count) => ContainsValue ? BitConverter.ToString(data.Array, data.Offset + offset, count) : null;
            public string AsBase64() => ContainsValue ? Convert.ToBase64String(data.Array, data.Offset, data.Count) : null;
            public string AsString() {
                try {
                    return Encoding.UTF8.GetString(data.Array, data.Offset, data.Count);
                } catch { return null; }
            }
            public int Count => data.Count;
            public ProtoReader GetReader(out ProtoReader.State state) {
                var ms = new MemoryStream(data.Array, data.Offset, data.Count, false);
                return ProtoReader.Create(out state, ms, null, null);
            }
            public ProtoReader GetReader() {
                var ms = new MemoryStream(data.Array, data.Offset, data.Count, false);
                return ProtoReader.Create(ms, null, null);
            }
            public bool ContainsValue => data.Array != null;
            public bool CouldBeProto() {
                if (!ContainsValue) return false;
                try {
                    using (var reader = GetReader(out var state)) {
                        int field;
                        while ((field = reader.ReadFieldHeader(ref state)) > 0) {
                            reader.SkipField(ref state);
                        }
                        return reader.GetPosition(ref state) == Count; // MemoryStream will let you seek out of bounds!
                    }
                } catch {
                    return false;
                }
            }
            public DecodeModel Slice(int offset, int count, int skipField = 0) => new DecodeModel(data.Array, Deep, data.Offset + offset, count, skipField);
        }

    }
}
