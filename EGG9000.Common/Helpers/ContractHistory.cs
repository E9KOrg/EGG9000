using EGG9000.Common.Database.Entities;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Prefarm;

namespace EGG9000.Common.Helpers {
    public class ContractScoring {
        public static List<UserContractScore> GetContractScores(List<Coop> coops, Contract contract) {

            var histories = new List<UserContractScore>();
            var skipped = 0;
            var xrefcount = 0;
            foreach(var coop in coops.Where(x => x.LastStatusUpdate != null)) {
                var coopStatus = coop.LastStatusUpdate;
                foreach(var xref in coop.UserCoopsXrefs.Where(x => x.JoinedCoop)) {
                    //var contribution = coop.LastStatusUpdate.Contributors.FirstOrDefault(x => x.UserId == xref.EggIncId);
                    //if(contribution != null) {
                    var maxAmount = contract.Details.GoalSets[(int)coop.League].Goals.OrderBy(x => x.TargetAmount).Last().TargetAmount;
                    //var lastStatus = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(xref.Status);
                    var archiveFarm = xref.User.EggIncAccounts.FirstOrDefault(x => x.Id == xref.EggIncId)?.Backup?.ArchivedFarms.FirstOrDefault(x => x.CoopId == xref.Coop.Name.ToLower());


                    Ei.ContractCoopStatusResponse.Types.ContributionInfo lastStatus = null;

                    if(xref.Status is not null)
                        lastStatus = JsonConvert.DeserializeObject<Ei.ContractCoopStatusResponse.Types.ContributionInfo>(xref.Status);

                    if(lastStatus is null)
                        lastStatus = coopStatus.Contributors.FirstOrDefault(x => x.UserId == xref.EggIncId);

                    if(lastStatus is null)
                        lastStatus = coopStatus.Contributors.FirstOrDefault(x => x.ContributionAmount == archiveFarm?.ContributionAmount);
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
                        Joined = DateTimeOffset.Now - (xref.User?.Registered ?? DateTimeOffset.Now),
                        Elite = coop.League == 0,
                        xref = xref,
                    });
                    //}
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
