using EGG9000.Onboarding.Steps;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Test.Onboarding {
    [TestClass]
    [TestCategory("Unit")]
    public class LoginWaiterTests {
        private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(10);

        // EGG9000.Test has <Nullable>enable</Nullable> but EGG9000.Bot does not, so the lookup
        // delegate is declared explicitly to keep the null literals from producing warnings.
        private static Func<CancellationToken, Task<string>> Lookup(Func<string> next) => _ => Task.FromResult(next());

        [TestMethod]
        public async Task Wait_LoginAlreadyPresent_ReturnsImmediately() {
            var result = await LoginWaiter.WaitForLoginAsync(
                Lookup(() => "user-1"), TimeSpan.FromSeconds(5), Poll, CancellationToken.None);
            Assert.AreEqual("user-1", result);
        }

        [TestMethod]
        public async Task Wait_LoginAppearsLater_ReturnsIt() {
            var calls = 0;
            var result = await LoginWaiter.WaitForLoginAsync(
                Lookup(() => ++calls < 3 ? null! : "user-2"),
                TimeSpan.FromSeconds(5), Poll, CancellationToken.None);
            Assert.AreEqual("user-2", result);
            Assert.IsGreaterThanOrEqualTo(3, calls);
        }

        [TestMethod]
        public async Task Wait_Timeout_ReturnsNull() {
            var sw = Stopwatch.StartNew();
            var result = await LoginWaiter.WaitForLoginAsync(
                Lookup(() => null!), TimeSpan.FromMilliseconds(80), Poll, CancellationToken.None);
            sw.Stop();
            Assert.IsNull(result);
            Assert.IsLessThan(TimeSpan.FromSeconds(5), sw.Elapsed);
        }

        [TestMethod]
        public async Task Wait_Cancelled_ReturnsNullWithoutThrowing() {
            using var cts = new CancellationTokenSource();
            var task = LoginWaiter.WaitForLoginAsync(
                Lookup(() => null!), TimeSpan.FromSeconds(30), Poll, cts.Token);
            await cts.CancelAsync();
            var result = await task;
            Assert.IsNull(result);
        }
    }
}
