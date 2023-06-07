using Cronos;

using EGG9000.Common.Commands;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class JobService : IHostedService {
        private readonly ILogger<JobService> _logger;
        private readonly IServiceProvider _provider;
        private List<Job> _jobs;
        private readonly Bugsnag.IClient _bugsnag;

        public JobService(ILogger<JobService> logger, IServiceProvider serviceProvider, Bugsnag.IClient bugsnag) {
            _logger = logger;
            _provider = serviceProvider;
            _bugsnag = bugsnag;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Starting JobService");

            _jobs = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
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
                    var jobs = _jobs.Where(x => x.NextRun < DateTimeOffset.Now).OrderBy(x => x.NextRun);
                    foreach(var job in jobs) {
                        job.NextRun = GetNextRun(job.Details.Cron);
                        _logger.LogInformation($"Running Job {job.DeclaringType.Name}.{job.Name}, Current time: {DateTimeOffset.Now.ToString("h:mm:ss:ff")}, next run at {job.NextRun.ToString("h:mm:ss:ff")}");
                        await RunJobAsync(job);
                    }
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    _logger.LogError(e, "Error running job");
                }
                var nextJob = _jobs.OrderBy(x => x.NextRun).FirstOrDefault();
                if(nextJob != null) {
                    var delay = nextJob.NextRun - DateTimeOffset.Now;
                    if(delay < TimeSpan.Zero) {
                        delay = TimeSpan.FromSeconds(1);
                    }
                    _logger.LogTrace($"Next job {nextJob.DeclaringType.Name}.{nextJob.Name} in {delay.TotalSeconds} seconds, delaying for {delay.TotalSeconds} seconds");
                    await Task.Delay((int)delay.TotalMilliseconds);
                } else {
                      _logger.LogTrace($"No jobs found, delaying for 1 second");
                    await Task.Delay(1000);
                }

            }
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _logger.LogInformation("Stopping JobService");
            return Task.CompletedTask;
        }

        private Task RunJobAsync(Job job) {
            var jobClass = ActivatorUtilities.CreateInstance(_provider, job.DeclaringType);
            job.DeclaringType.GetMethod(job.Name).Invoke(jobClass, null);
            return Task.CompletedTask;
        }

        private DateTimeOffset GetNextRun(string cron) {
            return CronExpression.Parse(cron, CronFormat.IncludeSeconds).GetNextOccurrence(DateTimeOffset.Now, TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time")).Value;
        }

        private class Job {
            public string Name { get; set; }
            public MethodInfo MethodInfo { get; set; }
            public JobAttribute Details { get; set; }
            public ParameterInfo[] Parameters { get; set; }
            public Type DeclaringType { get; set; }
            public DateTimeOffset NextRun { get; set; }
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class JobAttribute : System.Attribute {
        public string Cron;
        public JobAttribute(string cronExpression) {
            Cron = cronExpression;
        }
    }

}
