using EGG9000.Common.Database.Entities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class ContractScoring {
        public static List<UserContractScore>  GetContractScores(List<Coop> coops) {
            var histories = new List<UserContractScore>();

            foreach(var coop in coops.Where(x => x.LastStatusUpdate != null)) {
                foreach(var xref in coop.UserCoopsXrefs) {
                    var contribution = coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserId == xref.EggIncId);
                    if(contribution != null) {
                        histories.Add(new UserContractScore {
                            UserId = xref.UserId,
                            UserName = xref.User?.DiscordUsername,
                            SoulPower = contribution.SoulPower,
                            Coop = coop.Name,
                            EggsShipped = contribution.ContributionAmount,
                            TokensSpent = contribution.BoostTokensSpent,
                            Joined = DateTimeOffset.Now - (xref.User?.CreateOn ?? DateTimeOffset.Now),
                            Elite = coop.League == 0,
                            xref = xref,
                        });
                    }
                }
            }

            var historiesOrderedBySoulPower = histories.OrderBy(x => x.SoulPower).ToArray();
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
                averageEggs = averageEggs / count;

                historiesOrderedBySoulPower[currentIndex].Score = (float)(historiesOrderedBySoulPower[currentIndex].EggsShipped / averageEggs);
            }

            return historiesOrderedBySoulPower.OrderBy(x => x.Score).ToList();
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
            public bool Elite;
            public UserCoopXref xref;
        }
    }
}
