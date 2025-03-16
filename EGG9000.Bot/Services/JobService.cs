using Cronos;
using Humanizer;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class JobService(ILogger<JobService> logger, IServiceProvider serviceProvider, Bugsnag.IClient bugsnag) : IHostedService {
        private readonly ILogger<JobService> _logger = logger;
        private readonly IServiceProvider _provider = serviceProvider;
        private List<Job> _jobs;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;

        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Starting JobService");
            _jobs = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetExportedTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(JobAttribute), false).Length > 0)
                      .Select(x =>
                        new Job {
                            Name = x.Name, 
                            MethodInfo = x,  
                            Details = x.GetCustomAttribute<JobAttribute>(), 
                            Parameters = x.GetParameters(), 
                            DeclaringType = x.DeclaringType, 
                            NextRun = GetNextRun(x.GetCustomAttribute<JobAttribute>().Cron)
                        })
                      .ToList();

            _ = Main(cancellationToken);
            return Task.CompletedTask;
        }

        private async Task Main(CancellationToken cancellationToken) {
            while(!cancellationToken.IsCancellationRequested) {
                try {
                    var jobs = _jobs.Where(x => x.NextRun < DateTimeOffset.Now && !x.Running).OrderBy(x => x.NextRun);
                    foreach(var job in jobs) {
                        job.NextRun = GetNextRun(job.Details.Cron);
                        _logger.LogInformation("Running Job {jobDeclareType}.{jobName}, Current time: {currentTime}, next run at {nextRun}",
                            job.DeclaringType.Name, job.Name, $"{DateTimeOffset.Now:h:mm:ss:ff}", $"{job.NextRun:h:mm:ss:ff}");
                        
                        _ = Task.Run(async () => {
                            job.Running = true;
                            var timer = System.Diagnostics.Stopwatch.StartNew();
                            await RunJobAsync(job);
                            _logger.LogInformation("{jobDeclareType}.{jobName} took {timerHumanized}",
                                job.DeclaringType.Name, job.Name, timer.Elapsed.Humanize());
                            job.Running = false;
                        });
                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    _logger.LogError(e, "Error running job");
                }
                var nextJob = _jobs.OrderBy(x => x.NextRun).FirstOrDefault();
                if(nextJob != null) {
                    var delay = (nextJob.NextRun - DateTimeOffset.Now) + TimeSpan.FromMilliseconds(10);
                    if(delay < TimeSpan.Zero) {
                        delay = TimeSpan.FromSeconds(1);
                    }
                    _logger.LogTrace("Next job {jobDeclareType}.{jobName} in {delaySeconds} seconds, delaying for {delaySeconds} seconds",
                        nextJob.DeclaringType.Name, nextJob.Name, delay.TotalSeconds, delay.TotalSeconds);
                    await Task.Delay((int)delay.TotalMilliseconds, cancellationToken);
                } else {
                      _logger.LogTrace($"No jobs found, delaying for 1 second");
                    await Task.Delay(1000, cancellationToken);
                }

            }
        }

        public void RunJob(string jobName) {
            var job = _jobs.FirstOrDefault(x => x.Name == jobName);
            job.NextRun = DateTimeOffset.Now;
        }

        public bool StopJob(string jobName) {
            var job = _jobs.FirstOrDefault(x => x.Name == jobName);
            if(job is null) return false;
            job.NextRun = DateTimeOffset.Now.AddYears(1);
            return true;
        }
        public void StarJob(string jobName) {
            var job = _jobs.FirstOrDefault(x => x.Name == jobName);
            job.NextRun = GetNextRun(job.MethodInfo.GetCustomAttribute<JobAttribute>().Cron);
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Stopping JobService");
            return Task.CompletedTask;
        }

        private async Task RunJobAsync(Job job) {
            var jobClass = ActivatorUtilities.CreateInstance(_provider, job.DeclaringType);
            await (Task)job.DeclaringType.GetMethod(job.Name).Invoke(jobClass, null);
        }

        private static DateTimeOffset GetNextRun(string cron) {
            return CronExpression.Parse(cron, CronFormat.IncludeSeconds).GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
        }

        private class Job {
            public string Name { get; set; }
            public MethodInfo MethodInfo { get; set; }
            public JobAttribute Details { get; set; }
            public ParameterInfo[] Parameters { get; set; }
            public Type DeclaringType { get; set; }
            public DateTimeOffset NextRun { get; set; }
            public bool Running { get; set; }
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class JobAttribute(string cronExpression) : Attribute {
        public string Cron = cronExpression;
    }
}
