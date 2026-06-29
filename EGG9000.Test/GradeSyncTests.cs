using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using G = Ei.Contract.Types.PlayerGrade;

namespace EGG9000.Test {
    [TestClass]
    public class GradeSyncTests {
        [TestMethod]
        public void False_when_same() {
            Assert.IsFalse(GradeSync.ShouldUpdateGrade(G.GradeAa, G.GradeAa, guardUnset: true));
            Assert.IsFalse(GradeSync.ShouldUpdateGrade(G.GradeAa, G.GradeAa, guardUnset: false));
        }

        [TestMethod]
        public void True_when_changed_and_not_unset() {
            Assert.IsTrue(GradeSync.ShouldUpdateGrade(G.GradeA, G.GradeAa, guardUnset: true));
            Assert.IsTrue(GradeSync.ShouldUpdateGrade(G.GradeA, G.GradeAa, guardUnset: false));
        }

        [TestMethod]
        public void Unset_guarded_blocks_downgrade() {
            Assert.IsFalse(GradeSync.ShouldUpdateGrade(G.GradeAaa, G.GradeUnset, guardUnset: true));
        }

        [TestMethod]
        public void Unset_allowed_when_not_guarded() {
            Assert.IsTrue(GradeSync.ShouldUpdateGrade(G.GradeAaa, G.GradeUnset, guardUnset: false));
        }
    }
}
