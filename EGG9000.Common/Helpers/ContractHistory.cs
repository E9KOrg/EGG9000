using EGG9000.Common.Database.Entities;

using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;

namespace EGG9000.Common.Helpers {
    public class ContractScoring {
        public static List<UserContractScore> GetContractScores(List<Coop> coops, Contract contract, ILogger logger) {
            logger.LogInformation("Calculating scores for {contract}", contract.Name);
            var histories = new List<UserContractScore>();
            var skipped = 0;
            var xrefcount = 0;

            var gradesMoreThanADay = contract.Details.GradeSpecs.Where(x => x.LengthSeconds > TimeSpan.FromDays(1).TotalSeconds).Select(x => x.Grade);

            foreach(var coop in coops.Where(x => x.LastStatusUpdate != null && gradesMoreThanADay.Any(y => (int)y == x.League))) {
                var coopStatus = coop.LastStatusUpdate;
                foreach(var xref in coop.UserCoopsXrefs.Where(x => x.JoinedCoop)) {

                    var maxAmount = contract.Details.GradeSpecs[(int)coop.League - 1].Goals.OrderBy(x => x.TargetAmount).Last().TargetAmount;
                    var archiveFarm = xref.User.EggIncAccounts.FirstOrDefault(x => x.Id == xref.EggIncId)?.Backup?.ArchivedFarms.FirstOrDefault(x => x.CoopId == xref.Coop.Name.ToLower());

                    ContributionInfoCompact lastStatus = null;

                    if(xref.LastStatus is not null)
                        lastStatus = xref.LastStatus;

                    if(lastStatus is null && coopStatus.Contributors.Any(x => x.UserId == xref.EggIncId))
                        lastStatus = new ContributionInfoCompact(coopStatus.Contributors.FirstOrDefault(x => x.UserId == xref.EggIncId));

                    if(lastStatus is null && coopStatus.Contributors.Any(x => x.ContributionAmount == archiveFarm?.ContributionAmount))
                        lastStatus = new ContributionInfoCompact(coopStatus.Contributors.FirstOrDefault(x => x.ContributionAmount == archiveFarm?.ContributionAmount));

                    xrefcount++;

                    if(lastStatus is null) {
                        skipped++;
                        continue;
                    }

                    histories.Add(new UserContractScore {
                        UserId = xref.UserId,
                        UserName = xref.User?.DiscordUsername,
                        SoulPower = lastStatus.SoulPower,
                        Coop = coop.Name,
                        EggsShipped = Math.Min(lastStatus.ContributionAmount, maxAmount),
                        TokensSpent = lastStatus.BoostTokensSpent,
                        Joined = DateTimeOffset.UtcNow - (xref.User?.Registered ?? DateTimeOffset.UtcNow),
                        League = coop.League,
                        xref = xref,
                    });
                }
            }
            histories = [.. histories.GroupBy(x => x.UserId).Select(x => x.OrderBy(y => y.EggsShipped).First())];

            var historiesByGrade = histories.GroupBy(x => x.League).ToList();

            foreach(var grade in historiesByGrade) {
                var historiesOrderedBySoulPower = grade.OrderBy(x => x.SoulPower).ToArray();
                for(var currentIndex = 0; currentIndex < historiesOrderedBySoulPower.Length; currentIndex++) {
                    double averageEggs = 0;
var startAverage = Math.Max(0, currentIndex - 25);
var endAverage = Math.Min(historiesOrderedBySoulPower.Length, currentIndex + 25);
                    var count = 0;
                    for(var averageIndex = startAverage; averageIndex < endAverage; averageIndex++) {
                        if(averageIndex == currentIndex)
                            continue;
                        averageEggs += historiesOrderedBySoulPower[averageIndex].EggsShipped;
                        count++;
                    }
                    averageEggs /= count;

                    historiesOrderedBySoulPower[currentIndex].Score = count == 0 || averageEggs == 0 ? 1 : (float)(historiesOrderedBySoulPower[currentIndex].EggsShipped / averageEggs);
                }
            }

            return [.. histories.OrderBy(x => x.Score)];
        }
        public class UserContractScore {
            public Guid UserId;
            public string UserName;
            public double SoulPower;
            public string Coop;
            public double EggsShipped;
            public uint TokensSpent;
            public TimeSpan Joined;
            public float Score;
            public uint League;
            public UserCoopXref xref;
        }
    }
}
