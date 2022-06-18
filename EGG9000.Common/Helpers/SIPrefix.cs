using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EGG9000.Bot.Helpers {
    public class SIPrefix {
        public static PrefixDetails GetPrefix(double number) {
            //var lastPrefix = new PrefixDetails { Base = 0, Name = "" };
            //foreach(var prefix in Prefixes) {
            //    if (number < Math.Pow(10, prefix.Base)) {
            //        return lastPrefix;
            //    }

            //    lastPrefix = prefix;
            //}
            //return lastPrefix;
            int exponent = number == 0 ? 0 : (int)Math.Floor((Math.Log10(Math.Abs(number))));
            var prefix = Prefixes.FirstOrDefault(x => exponent < x.Base + 3);
            if(prefix == null)
                prefix = Prefixes.Last();
            prefix.Rank = exponent - prefix.Base + 1;
            return prefix;
        }

        public class PrefixDetails {
            public string Name { get; set; }
            public int Base { get; set; }
            public int Rank { get; set; }
        }

        public static List<PrefixDetails> Prefixes {
            get {
                return new List<PrefixDetails> {
                    new PrefixDetails{ Name="kilo", Base = 3},
                    new PrefixDetails{ Name="mega", Base = 6},
                    new PrefixDetails{ Name="giga", Base = 9},
                    new PrefixDetails{ Name="tera", Base = 12},
                    new PrefixDetails{ Name="peta", Base = 15},
                    new PrefixDetails{ Name="exa", Base = 18},
                    new PrefixDetails{ Name="zetta", Base = 21},
                    new PrefixDetails{ Name="yotta", Base = 24},
                    new PrefixDetails{ Name="xenna", Base = 27},
                    new PrefixDetails{ Name="wecca", Base = 30},
                    new PrefixDetails{ Name="venda", Base = 33},
                };
            }
        }
    }
}
