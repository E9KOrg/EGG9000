using Discord;
using Discord.WebSocket;

using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using EGG9000.Bot.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;


using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class TestUpdater : _UpdaterBase<TestUpdater> {
        public TestUpdater(
            IServiceProvider provider
        ) : base(TimeSpan.FromSeconds(10), TimeSpan.Zero, provider) {
        }

        public override async Task Run(object state, CancellationToken cancellationToken) {
            for(var i = 0; i < 10000; i++) {
                await Task.Delay(500);
                if(cancellationToken.IsCancellationRequested) {
                    Console.WriteLine("Cancellation Requested");
                    await Task.Delay(2000);
                    Console.WriteLine("Cancellation Finished");
                    return;
                }
                Console.WriteLine("Test Updater Ping");
            }
        }
    }
}
