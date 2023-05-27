using NLog;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace EGG9000.Bot {
    public static class ArgumentsHelper {
public static List<KeyValuePair<int, string>> bignums = new List<KeyValuePair<int, string>> {
    new KeyValuePair<int, string>(0, ""),
    new KeyValuePair<int, string>(3, "K"),
    new KeyValuePair<int, string>(6, "M"),
    new KeyValuePair<int, string>(9, "B"),
    new KeyValuePair<int, string>(12, "T"),
    new KeyValuePair<int, string>(15, "q"),
    new KeyValuePair<int, string>(18, "Q"),
    new KeyValuePair<int, string>(21, "s"),
    new KeyValuePair<int, string>(24, "S"),
    new KeyValuePair<int, string>(27, "o"),
    new KeyValuePair<int, string>(30, "N"),
    new KeyValuePair<int, string>(33, "d"),
    new KeyValuePair<int, string>(36, "u"),
    new KeyValuePair<int, string>(39, "D"),
    new KeyValuePair<int, string>(42, "Td"),
    new KeyValuePair<int, string>(45, "qd"),
    new KeyValuePair<int, string>(48, "Qd"),
    new KeyValuePair<int, string>(51, "sd"),
    new KeyValuePair<int, string>(54, "Sd"),
    new KeyValuePair<int, string>(57, "Od"),
    new KeyValuePair<int, string>(60, "Nd"),
    new KeyValuePair<int, string>(63, "V"),
    new KeyValuePair<int, string>(66, "uV"),
    new KeyValuePair<int, string>(69, "dV"),
    new KeyValuePair<int, string>(72, "tV"),
    new KeyValuePair<int, string>(75, "qV"),
    new KeyValuePair<int, string>(78, "QV"),
};

        public static string ToEggString(this double number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            return NumberToString(number, showdecimalplaces, numberOfDecimalPlaces);
        }

        public static string ToEggString(this ulong number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            return NumberToString(number, showdecimalplaces, numberOfDecimalPlaces);
        }

        public static string NumberToString(double number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            var negative = number < 0;
            if(negative)
                number *= -1;
            var oom = number == 0 ? 0 : Math.Floor(Math.Log10(number)) - Math.Floor(Math.Log10(number)) % 3;
            var remainder = Math.Floor(Math.Log10(number)) % 3;

            var suffix = bignums.Any(x => x.Key == oom) ? bignums.First(x => x.Key == oom) : bignums.Last();

            number /= Math.Pow(10.0, oom);

            var outString = new StringBuilder();
            if(numberOfDecimalPlaces != -1)
                outString.Append(number.ToString($"F{numberOfDecimalPlaces}"));
            else if(showdecimalplaces)
                outString.Append(number.ToString("G3"));
            else if(remainder == 0)
                outString.Append(number.ToString("N1"));
            else if(suffix.Key != bignums.Last().Key && number.ToString("N0") == "1,000") {
                outString.Append("1");
                suffix = bignums.First(x => x.Key == oom + 3);
            } else
                outString.Append(number.ToString("N0"));

            if(negative)
                outString.Insert(0, "-");
            outString.Append(suffix.Value);
            return outString.ToString();

        }

        public static string NumberToStringOld(double number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            var negative = number < 0;
            if(negative)
                number *= -1;
            var nums = bignums.OrderByDescending(x => x.Key).ToList();
            var outString = "";
            for(var i = 0; i < nums.Count(); i++) {
                var num = nums[i];
                if(number >= Math.Pow(10.0, num.Key)) {
                    var numberPortion = number / Math.Pow(10.0, num.Key);
                    if(numberOfDecimalPlaces != -1) {
                        outString = numberPortion.ToString($"F{numberOfDecimalPlaces}") + num.Value;
                    } else if(showdecimalplaces) {
                        var o = numberPortion.ToString("G3");
                        outString = o + num.Value;
                    } else {
                        if(numberPortion > 10 && numberPortion < 1000) {
                            if(numberPortion.ToString("N0") == "1,000") {
                                outString = "1" + nums[i - 1].Value;
                            } else {
                                outString = numberPortion.ToString("N0") + num.Value;
                            }
                        } else {
                            outString = numberPortion.ToString("N1") + num.Value;
                        }
                    }
                    break;
                }

            }
            if(outString == "")
                outString = number.ToString("0");
            if(negative)
                outString = $"-{outString}";
            return outString;
        }
        public static double GetNumberOfZeros(this double number) {
            var places = 0;
            while(number >= 10) {
                number /= 10;
                places++;
            }
            return places + (number / 10);
        }

        public static BigInteger NumberFromString(string arg) {
            var size = arg[arg.Length - 1];
            var numberPortion = arg.Substring(0, arg.Length - 1);

            if(BigInteger.TryParse(numberPortion, out var number)) {
                switch(size) {
                    case 'B':
                        return number * BigInteger.Pow(10, 9);
                    case 'T':
                        return number * BigInteger.Pow(10, 12);
                    case 'q':
                        return number * BigInteger.Pow(10, 15);
                    default:
                        throw new UnableToParseNumberExecption();
                }
            } else {
                throw new UnableToParseNumberExecption();
            }
        }
public static double NumberFromStringDouble(string arg) {
    var regex = new Regex(@"([\d\.]+)(\w*)");
    var match = regex.Match(arg);
    if(match.Success) {
        var size = match.Groups[2].Value;
        var numberPortion = match.Groups[1].Value;

        var number = double.Parse(numberPortion);


        if(bignums.Any(x => x.Value == size)) {
            var value = bignums.First(x => x.Value == size);
            return number * Math.Pow(10, value.Key);
        }
    }
    throw new UnableToParseNumberExecption();
}
    }
    public class UnableToParseNumberExecption : Exception {

    }
}
