using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class SeasonInfoDetailsTests {

        private static Ei.ContractSeasonInfo SampleSeason() {
            var season = new Ei.ContractSeasonInfo {
                Id = "winter_2026",
                Name = "Winter 2026",
                StartTime = 1_760_000_000
            };
            var grade = new Ei.ContractSeasonInfo.Types.GoalSet { Grade = Ei.Contract.Types.PlayerGrade.GradeAaa };
            grade.Goals.Add(new Ei.ContractSeasonGoal { RewardType = Ei.RewardType.EggsOfProphecy, RewardAmount = 2, Cxp = 100 });
            grade.Goals.Add(new Ei.ContractSeasonGoal { RewardType = Ei.RewardType.Cash, RewardAmount = 500, Cxp = 50 });
            season.GradeGoals.Add(grade);
            return season;
        }

        [TestMethod]
        public void ApplyDetails_KeepsPeGoalFilterAndMath() {
            var info = SeasonInfo.FromProto(SampleSeason());

            Assert.AreEqual("winter_2026", info.Id);
            Assert.AreEqual("Winter 2026", info.Name);
            Assert.AreEqual(DateTimeOffset.UnixEpoch.AddSeconds(1_760_000_000), info.StartTime);
            Assert.AreEqual(2, info.GetPeEarned(Ei.Contract.Types.PlayerGrade.GradeAaa, 150));
            Assert.AreEqual(0, info.GetPeEarned(Ei.Contract.Types.PlayerGrade.GradeAaa, 50));
            Assert.AreEqual(2, info.GetMaxPe(Ei.Contract.Types.PlayerGrade.GradeAaa));
        }

        [TestMethod]
        public void Details_RoundTripsFromBlob() {
            var stored = SeasonInfo.FromProto(SampleSeason())._response;
            var reloaded = new SeasonInfo { _response = stored };

            Assert.AreEqual("winter_2026", reloaded.Details.Id);
            Assert.AreEqual(1, reloaded.Details.GradeGoals.Count);
        }

        [TestMethod]
        public void Response_IsStableForChangeDetection() {
            var info = SeasonInfo.FromProto(SampleSeason());

            Assert.AreEqual(JsonConvert.SerializeObject(SampleSeason()), info._response);
        }

        [TestMethod]
        public void Details_NullSafe_OnPreMigrationRow() {
            Assert.IsNull(new SeasonInfo().Details);
        }
    }
}
