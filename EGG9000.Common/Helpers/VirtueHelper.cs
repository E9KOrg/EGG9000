using EGG9000.Common.Database;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class VirtueHelper {
        public static double[] thresholds = new[] { 0, 50e6, 1e9, 10e9, 70e9, 500e9, 2e12, 7e12, 20e12, 60e12, 150e12, 500e12,

        1.5e15, 4e15, 10e15,25e15,50e15,100e15
        };

        public static VirtueEggStats EggStats(CustomBackup backup, Ei.Egg egg) {
            return EggStats(backup.VirtueEggsDelivered[(int)egg - 50]);
            //for(var j = thresholds.Length - 1; j >= 0; j--) {
            //    if(backup.VirtueEggsDelivered[(int)egg - 50] >= thresholds[j]) {
            //        if(j < thresholds.Length - 1) {
            //            var progress = backup.VirtueEggsDelivered[(int)egg - 50] / thresholds[j + 1];
            //            return new VirtueEggStats {
            //                Egg = egg, Delivered = backup.VirtueEggsDelivered[(int)egg - 50], Level = j, Progress = progress, NextThreshold = thresholds[j + 1]
            //            };
            //        } else {
            //            var level = CurrentLevel(backup.VirtueEggsDelivered[(int)egg - 50]);
            //            var nextThreshold = GetThresholdForLevel(level + 1);
            //            var progress = backup.VirtueEggsDelivered[(int)egg - 50] / nextThreshold;
            //            return new VirtueEggStats {
            //                Egg = egg, Delivered = backup.VirtueEggsDelivered[(int)egg - 50], Level = level, Progress = progress, NextThreshold = nextThreshold
            //            };
            //        }
            //    }
            //}
            //throw new Exception("Error finding next threshold");
        }

        public static VirtueEggStats EggStats(double eggsShipped) {
            for(var j = thresholds.Length - 1; j >= 0; j--) {
                if(eggsShipped >= thresholds[j]) {
                    if(j < thresholds.Length - 1) {
                        var progress = eggsShipped / thresholds[j + 1];
                        var t2 = GetThresholdForLevel(j + 2);
                        var p2 = eggsShipped / t2;
                        var t3 = GetThresholdForLevel(j + 3);
                        var p3 = eggsShipped / t3;
                        return new VirtueEggStats {
                            Delivered = eggsShipped, Level = j, Progress = progress, NextThreshold = thresholds[j + 1], Progress2 = p2, NextThreshold2 = t2, Progress3 = p3, NextThreshold3 = t3
                        };
                    } else {
                        var level = CurrentLevel(eggsShipped);
                        var nextThreshold = GetThresholdForLevel(level + 1);
                        var progress = eggsShipped / nextThreshold;
                        var t2 = GetThresholdForLevel(level + 2);
                        var p2 = eggsShipped / t2;
                        var t3 = GetThresholdForLevel(level + 3);
                        var p3 = eggsShipped / t3;
                        return new VirtueEggStats {
                            Delivered = eggsShipped, Level = level, Progress = progress, NextThreshold = nextThreshold, Progress2 = p2, NextThreshold2 = t2, Progress3 = p3, NextThreshold3 = t3
                        };
                    }
                }
            }
            throw new Exception("Error finding next threshold");
        }




        public static int CurrentLevel(double virtueEggsDelivered) {
            if(virtueEggsDelivered < thresholds[1]) return 0;
            for(var i = 0; i < 9999; i++) {
                if(i < thresholds.Length) {
                    if(virtueEggsDelivered <= thresholds[i]) return i - 1;
                } else if(virtueEggsDelivered < GetThresholdForLevel(i)) return i - 1;
            }
            return 9999;
        }

        public static double GetThresholdForLevel(int level) {
            if(level < thresholds.Length) return thresholds[level];
            var m = level - (thresholds.Length - 2);
            return 1e17 + (m - 1) * 5e16 + ((m - 1) * (m - 2) / 2) * 1e16;
        }

        public static TimeSpan GetTimeToThreshold(double eggsDelivered, double eggsPerSecond, double threshold, DateTimeOffset lastBackupTime) {
            if(eggsPerSecond <= 0) return TimeSpan.MaxValue;
            if(eggsDelivered >= threshold) return TimeSpan.Zero;
            var seconds = (threshold - eggsDelivered) / eggsPerSecond;

            if(seconds >= TimeSpan.FromDays(365).TotalSeconds) return TimeSpan.MaxValue;

            return lastBackupTime.AddSeconds(seconds) - DateTimeOffset.UtcNow;
        }

        public static (TimeSpan timeTillHabsFull, TimeSpan timeTillShippingFull) GetTimeTillFull(double eggsPerSecond, DateTimeOffset lastBackupTime, double currentChickens, double maxChickens, double maxShipping, double totalihr) {
            if(totalihr == 0)
                return (TimeSpan.MaxValue, TimeSpan.MaxValue);
            var ratePerChicken = eggsPerSecond / currentChickens;
            var rateAtMaxChickens = ratePerChicken * maxChickens;



            var shippingCapcityLeft = maxShipping - eggsPerSecond;
            var capacityGrowthRate = ratePerChicken * totalihr;

            var secondsSinceBackup = (DateTimeOffset.UtcNow - lastBackupTime).TotalSeconds;

            var timeTillShippingFull = capacityGrowthRate == 0 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(Math.Max(0,shippingCapcityLeft / capacityGrowthRate - secondsSinceBackup));
            var timeTillHabsFull = totalihr == 0 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(Math.Max(0,(maxChickens - currentChickens) / totalihr - secondsSinceBackup));

            return (timeTillHabsFull, timeTillShippingFull);
        }

        public static TimeSpan GetTimeToThreshold(double eggsDelivered, double eggsPerSecond, double threshold, DateTimeOffset lastBackupTime, double currentChickens, double maxChickens, double maxShipping, double totalihr) {
            if(totalihr == 0)
                return GetTimeToThreshold(eggsDelivered, eggsPerSecond, threshold, lastBackupTime);
            if(eggsPerSecond <= 0) return TimeSpan.MaxValue;
            if(eggsDelivered >= threshold) return TimeSpan.Zero;



            var ratePerChicken = eggsPerSecond / currentChickens;
            var rateAtMaxChickens = ratePerChicken * maxChickens;

            var cappedDeliver = 0d;
            var secondsTillCap = 0d;

            if(rateAtMaxChickens > maxShipping) {
                cappedDeliver = maxShipping - eggsPerSecond;
                var shippingCapcityLeft = maxShipping - eggsPerSecond;
                var capacityGrowthRate = ratePerChicken * totalihr;
                secondsTillCap = shippingCapcityLeft / capacityGrowthRate;
            } else {
                cappedDeliver = rateAtMaxChickens - eggsPerSecond;
                secondsTillCap = (maxChickens - currentChickens) / totalihr;
            }

            var amountAtCap = secondsTillCap * cappedDeliver / 2 + secondsTillCap * eggsPerSecond;
            var thresholdLeft = threshold - eggsDelivered;

            double seconds;
            if(amountAtCap >= thresholdLeft) {
                //seconds = (2 * thresholdLeft) / (2 * eggsPerSecond + cappedDeliver);
                var a = cappedDeliver / secondsTillCap;
                var b = 2 * eggsPerSecond;
                var c = -2 * thresholdLeft;
                var root1 = (-b + Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
                var root2 = (-b - Math.Sqrt(b * b - 4 * a * c)) / (2 * a);
                seconds = Math.Max(root1, root2);
            } else {
                var thresholdLeftAfterCap = thresholdLeft - amountAtCap;
                seconds = thresholdLeftAfterCap / (cappedDeliver + eggsPerSecond) + secondsTillCap;
            }

            if(seconds < (DateTimeOffset.UtcNow - lastBackupTime).TotalSeconds) return TimeSpan.Zero;
            return lastBackupTime.AddSeconds(seconds) - DateTimeOffset.UtcNow;
        }
    }


    public class VirtueEggStats {
        public double Delivered { get; set; }
        public int Level { get; set; }
        public double Progress { get; set; }
        public double NextThreshold { get; set; }
        public double Progress2 { get; set; }
        public double NextThreshold2 { get; set; }
        public double Progress3 { get; set; }
        public double NextThreshold3 { get; set; }
    }
}
