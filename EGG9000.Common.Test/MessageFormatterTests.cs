using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using EGG9000.Common.Helpers;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EGG9000.Common.Test {
    [TestClass]
    public class MessageFormatterTests {
        private static Dictionary<string, string> Values(params (string, string)[] pairs) {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach(var (k, v) in pairs) d[k] = v;
            return d;
        }

        [TestMethod]
        public async Task Value_token_substituted() {
            var result = await MessageFormatter.FormatAsync("hi {{user}}!", Values(("user", "@bob")));
            Assert.AreEqual("hi @bob!", result);
        }

        [TestMethod]
        public async Task Unknown_value_token_left_verbatim() {
            var result = await MessageFormatter.FormatAsync("x {{nope}} y", Values());
            Assert.AreEqual("x {{nope}} y", result);
        }

        [TestMethod]
        public async Task Command_token_resolved() {
            var result = await MessageFormatter.FormatAsync("use {{command:faq}}", null, resolveCommand: n => Task.FromResult("/" + n));
            Assert.AreEqual("use /faq", result);
        }

        [TestMethod]
        public async Task Emoji_found_resolved() {
            var result = await MessageFormatter.FormatAsync("egg {{emoji:egg}}", null, resolveEmoji: _ => Task.FromResult("<:egg:1>"));
            Assert.AreEqual("egg <:egg:1>", result);
        }

        [TestMethod]
        public async Task Emoji_missing_left_verbatim() {
            var result = await MessageFormatter.FormatAsync("egg {{emoji:x}}", null, resolveEmoji: _ => Task.FromResult<string>(null!));
            Assert.AreEqual("egg {{emoji:x}}", result);
        }

        [TestMethod]
        public async Task Unknown_prefix_left_verbatim() {
            var result = await MessageFormatter.FormatAsync("a {{foo:bar}} b", Values());
            Assert.AreEqual("a {{foo:bar}} b", result);
        }

        [TestMethod]
        public async Task Mixed_tokens() {
            var result = await MessageFormatter.FormatAsync(
                "{{user}} is {{rank}} {{command:c}} {{emoji:e}}",
                Values(("user", "@bob"), ("rank", "Kilofarmer II")),
                resolveCommand: n => Task.FromResult("/" + n),
                resolveEmoji: _ => Task.FromResult("<:e:9>"));
            Assert.AreEqual("@bob is Kilofarmer II /c <:e:9>", result);
        }

        [TestMethod]
        public async Task Null_or_empty_text_returned_as_is() {
            Assert.AreEqual("", await MessageFormatter.FormatAsync("", Values()));
            Assert.IsNull(await MessageFormatter.FormatAsync(null, Values()));
        }
    }
}
