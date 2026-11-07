using EggSharp.Api;
using System;

namespace EGG9000.Common.EggIncAPI {
    internal sealed class EggIncApiMetrics : IEggIncApiMetrics {
        public void CallStarted(string path) => Services.RuntimeMetrics.AddApiCalls();

        public void CallFailed(string path, Exception exception) => Services.RuntimeMetrics.AddApiFailures();
    }
}
