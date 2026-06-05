using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using EGG9000.Common.Helpers;

namespace EGG9000.Common.Helpers.AfxSets {
    public static class AfxSetsHash {
        // Order-sensitive, stable hash over set contents (id/tier/rarity + stones).
        public static string Compute(List<List<EggIncArtifactInstance>> sets) {
            var sb = new StringBuilder();
            foreach(var set in sets ?? new()) {
                sb.Append('|');
                foreach(var a in set) {
                    sb.Append(a.Id).Append(':').Append(a.Tier).Append(':').Append(a.Rarity).Append('(');
                    foreach(var s in a.Stones ?? new()) sb.Append(s.Id).Append(':').Append(s.Tier).Append(',');
                    sb.Append(')');
                }
            }
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }
}
