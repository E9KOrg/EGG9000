using System.Linq;

using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    public class EditFaqValidationTests {
        [TestMethod]
        public void ParseKeywords_trims_and_drops_empties() {
            var result = EditFaqValidation.ParseKeywords("a, b ,,c");
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, result);
            Assert.IsEmpty(EditFaqValidation.ParseKeywords(""));
            Assert.IsEmpty(EditFaqValidation.ParseKeywords(null));
        }

        [TestMethod]
        public void ValidateKeywords_flags_too_long() {
            Assert.IsNull(EditFaqValidation.ValidateKeywords(new[] { "fine", "alsofine" }));
            var tooLong = new string('x', FAQHelper.MAX_KEYWORD_LENGTH + 1);
            Assert.IsNotNull(EditFaqValidation.ValidateKeywords(new[] { "ok", tooLong }));
        }

        [TestMethod]
        public void NormalizeColor_handles_valid_empty_and_invalid() {
            Assert.AreEqual("a1b2c3", EditFaqValidation.NormalizeColor("#A1B2C3").Value);
            Assert.AreEqual("d43500", EditFaqValidation.NormalizeColor("d43500").Value);

            var (empty, emptyErr) = EditFaqValidation.NormalizeColor("");
            Assert.AreEqual("", empty);
            Assert.IsNull(emptyErr);

            var (val, err) = EditFaqValidation.NormalizeColor("zzz");
            Assert.IsNull(val);
            Assert.IsNotNull(err);
        }
    }
}
