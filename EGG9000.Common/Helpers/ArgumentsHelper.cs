using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace EGG9000.Bot {
    public static class ArgumentsHelper {

        public static readonly List<KeyValuePair<int, string>> bignums = [
            new(0, ""),
            new(3, "K"),
            new(6, "M"),
            new(9, "B"),
            new(12, "T"),
            new(15, "q"),
            new(18, "Q"),
            new(21, "s"),
            new(24, "S"),
            new(27, "o"),
            new(30, "N"),
            new(33, "d"),
            new(36, "u"),
            new(39, "D"),
            new(42, "Td"),
            new(45, "qd"),
            new(48, "Qd"),
            new(51, "sd"),
            new(54, "Sd"),
            new(57, "Od"),
            new(60, "Nd"),
            new(63, "V"),
            new(66, "uV"),
            new(69, "dV"),
            new(72, "tV"),
            new(75, "qV"),
            new(78, "QV"),
        ];

        public static string ToEggString(this double number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            return NumberToString(number, showdecimalplaces, numberOfDecimalPlaces);
        }

        public static string ToEggString(this ulong number, bool showdecimalplaces = false, int numberOfDecimalPlaces = -1) {
            return NumberToString(number, showdecimalplaces, numberOfDecimalPlaces);
        }

        public static string ToEggStringD(this double number, int numberOfDigits) {
            return NumberToStringD(number, numberOfDigits);
        }

        public static string ToEggStringD(this ulong number, int numberOfDigits) {
            return NumberToStringD(number, numberOfDigits);
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
                outString.Append('1');
                suffix = bignums.First(x => x.Key == oom + 3);
            } else
                outString.Append(number.ToString("N0"));

            if(negative)
                outString.Insert(0, "-");
            outString.Append(suffix.Value);
            return outString.ToString();

        }

        public static string NumberToStringD(double number, int numberOfDigits) {
            var negative = number < 0;
            if(negative)
                number *= -1;
            var oom = number == 0 ? 0 : Math.Floor(Math.Log10(number)) - Math.Floor(Math.Log10(number)) % 3;
            var remainder = Math.Floor(Math.Log10(number)) % 3;

            var suffix = bignums.Any(x => x.Key == oom) ? bignums.First(x => x.Key == oom) : bignums.Last();

            number /= Math.Pow(10.0, oom);

            var outString = new StringBuilder();

            // Calculate the number of digits before the decimal point
            int digitsBeforeDecimal = (int)Math.Floor(Math.Log10(number)) + 1;

            // Calculate the number of decimal places required
            int decimalPlaces = Math.Max(0, numberOfDigits - digitsBeforeDecimal);

            // Using custom format specifier to specify the total number of digits including decimal places
            outString.Append(number.ToString($"0.{new string('0', decimalPlaces)}"));

            if(negative)
                outString.Insert(0, "-");
            outString.Append(suffix.Value);
            return outString.ToString();
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
                return size switch {
                    'B' => number * BigInteger.Pow(10, 9),
                    'T' => number * BigInteger.Pow(10, 12),
                    'q' => number * BigInteger.Pow(10, 15),
                    _ => throw new UnableToParseNumberExecption(),
                };
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
