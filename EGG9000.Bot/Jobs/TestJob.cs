using EGG9000.Bot.Services;

using Microsoft.Extensions.Logging;

using Quartz;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Jobs {

    public class TestJob {
        private readonly ILogger<TestJob> _logger;

        public TestJob(ILogger<TestJob> logger) {
            _logger = logger;
        }

        //Cron Expression Includes Seconds
        //[Job("*/10 * * * * *")]
        //public Task Test1() {
        //    _logger.LogInformation("Hello from TestJob Test1!");
        //    return Task.CompletedTask;
        //}
    }
}
