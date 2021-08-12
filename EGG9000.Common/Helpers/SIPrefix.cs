using System;
using System.Collections.Generic;
using System.Text;

namespace EGG9000.Bot.Helpers {
    public class SIPrefix {
        public static PrefixDetails GetPrefix(double number) {
            var lastPrefix = new PrefixDetails { Base = 0, Name = "" };
            foreach(var prefix in Prefixes) {
                if (number < Math.Pow(10, prefix.Base))
                    return lastPrefix;
                lastPrefix = prefix;
            }
            return lastPrefix;
        }

        public class PrefixDetails {
            public string Name { get; set; }
            public int Base { get; set; }
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
                };
            }
        }
    }
}
