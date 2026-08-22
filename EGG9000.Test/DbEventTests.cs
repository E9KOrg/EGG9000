using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

namespace EGG9000.Test {
    [TestClass]
    [TestCategory("Unit")]
    public class DbEventTests {

        private static Ei.EggIncEvent SampleEvent() {
            return new Ei.EggIncEvent {
                Identifier = "event-1",
                Type = "epic-research-sale",
                Multiplier = 0.5,
                Subtitle = "Epic Research Sale",
                SecondsRemaining = 3600,
                CcOnly = true
            };
        }

        [TestMethod]
        public void ApplyDetails_SyncsMirrorColumns_ButNotEnds() {
            var dbEvent = new DBEvent { Ends = DateTimeOffset.UnixEpoch };
            dbEvent.ApplyDetails(SampleEvent());

            Assert.AreEqual("event-1", dbEvent.Identifier);
            Assert.AreEqual("epic-research-sale", dbEvent.Type);
            Assert.AreEqual(0.5, dbEvent.Multiplier);
            Assert.AreEqual("Epic Research Sale", dbEvent.Subtitle);
            Assert.IsTrue(dbEvent.CcOnly);
            Assert.AreEqual(DateTimeOffset.UnixEpoch, dbEvent.Ends);
            Assert.IsNotNull(dbEvent._response);
        }

        [TestMethod]
        public void Ctor_SetsEndsFromSecondsRemaining() {
            var before = DateTimeOffset.UtcNow;
            var dbEvent = new DBEvent(SampleEvent());

            Assert.IsTrue(dbEvent.Ends >= before.AddSeconds(3600));
            Assert.IsTrue(dbEvent.Ends <= DateTimeOffset.UtcNow.AddSeconds(3600));
        }

        [TestMethod]
        public void Details_RoundTripsFromBlob() {
            var stored = new DBEvent(SampleEvent())._response;
            var reloaded = new DBEvent { _response = stored };

            Assert.AreEqual("event-1", reloaded.Details.Identifier);
            Assert.AreEqual(3600d, reloaded.Details.SecondsRemaining);
        }

        [TestMethod]
        public void Details_NullSafe_OnPreMigrationRow() {
            Assert.IsNull(new DBEvent().Details);
        }
    }
}
