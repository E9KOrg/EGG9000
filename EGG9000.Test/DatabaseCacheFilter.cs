using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

namespace EGG9000.Test {
    // Guards the RefreshUserCache predicate change: the old filter was
    //   LastModified > cutoff OR CreateOn > cutoff
    // and was simplified to
    //   LastModified > cutoff
    // The simplification is only sound because every persisted user satisfies
    // LastModified >= CreateOn (the Added hook stamps LastModified at insert time and
    // CreateOn never changes after). These tests pin that equivalence so a future change to
    // either column's lifecycle that breaks the invariant fails loudly.
    [TestClass]
    public class DatabaseCacheFilterTests {
        private static readonly DateTimeOffset Base = new(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

        private static DateTimeOffset At(int seconds) => Base.AddSeconds(seconds);

        private static bool OldPredicate(DBUser u, DateTimeOffset cutoff)
            => u.LastModified > cutoff || u.CreateOn > cutoff;

        private static bool NewPredicate(DBUser u, DateTimeOffset cutoff)
            => DatabaseCache.UpdatedSince(cutoff).Compile()(u);

        [TestMethod]
        public void NewMatchesOldForAllInvariantRespectingUsers() {
            var cutoff = At(0);
            var offsets = new[] { -2, -1, 0, 1, 2 };
            var cases = new List<(int created, int modified)>();
            foreach(var created in offsets)
                foreach(var modified in offsets)
                    if(modified >= created) // invariant: LastModified >= CreateOn
                        cases.Add((created, modified));

            foreach(var (created, modified) in cases) {
                var u = new DBUser { CreateOn = At(created), LastModified = At(modified) };
                Assert.AreEqual(OldPredicate(u, cutoff), NewPredicate(u, cutoff),
                    $"Divergence for CreateOn={created}s LastModified={modified}s relative to cutoff");
            }
        }

        [TestMethod]
        public void CutoffIsStrictlyGreaterNotInclusive() {
            var cutoff = At(0);
            var atCutoff = new DBUser { CreateOn = At(0), LastModified = At(0) };
            var justAfter = new DBUser { CreateOn = At(0), LastModified = At(1) };

            Assert.IsFalse(NewPredicate(atCutoff, cutoff), "LastModified == cutoff must not count as updated");
            Assert.IsTrue(NewPredicate(justAfter, cutoff), "LastModified one tick past cutoff must count as updated");
        }

        [TestMethod]
        public void FreshlyCreatedUserIsPickedUp() {
            // Registration sets CreateOn = LastModified at insert; a user created after the last
            // refresh must be caught by the LastModified-only predicate.
            var cutoff = At(0);
            var freshUser = new DBUser { CreateOn = At(5), LastModified = At(5) };
            Assert.IsTrue(NewPredicate(freshUser, cutoff));
        }

        [TestMethod]
        public void PredicatesDivergeOnlyWhenInvariantViolated() {
            // The single shape where dropping the CreateOn branch changes the result is
            // LastModified < CreateOn, which the Added hook makes impossible. Documented here so
            // the precondition is explicit: if this ever happens the simplification is unsound.
            var cutoff = At(0);
            var invariantViolating = new DBUser { CreateOn = At(1), LastModified = At(-1) };

            Assert.IsTrue(OldPredicate(invariantViolating, cutoff), "old filter would have caught it via CreateOn");
            Assert.IsFalse(NewPredicate(invariantViolating, cutoff), "new filter relies on LastModified >= CreateOn");
        }
    }
}
