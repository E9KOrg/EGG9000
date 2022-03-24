using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public static class TimespanStringParser {
        public static DateTimeOffset AddTimeSpanString(this string timeSpanString, DateTimeOffset time) {
            var timeSpanSplit = new Regex(@"([\d\.]+)\s?(\w+)").Matches(timeSpanString);


            var timeValue = double.Parse(timeSpanSplit.First().Groups[1].Value);

            DateTimeOffset returnTime;

            switch(timeSpanSplit.First().Groups[2].Value) {
                case "s":
                case "sec":
                case "second":
                case "seconds":
                    returnTime = time.AddSeconds(timeValue);
                    break;
                case "m":
                case "min":
                case "minute":
                case "minutes":
                    returnTime = time.AddMinutes(timeValue);
                    break;
                case "h":
                case "hour":
                case "hours":
                    returnTime = time.AddHours(timeValue);
                    break;
                case "d":
                case "day":
                case "days":
                    returnTime = time.AddDays(timeValue);
                    break;
                case "w":
                case "week":
                case "weeks":
                    returnTime = time.AddDays(timeValue * 7);
                    break;
                case "M":
                case "mon":
                case "month":
                case "months":
                    returnTime = time.AddDays(timeValue * 30);
                    break;
                default:
                    throw new ArgumentException($"Invalid timeSpanString {timeSpanString}");

            }

            return returnTime;
        }
    }
}
