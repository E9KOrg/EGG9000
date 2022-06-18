using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Common.Helpers;
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class CoopDeleteChannel : _UpdaterBase {
        private IConfiguration Configuration;

        public CoopDeleteChannel(IConfiguration Configuration, DiscordHostedService client,
            Bugsnag.IClient bugsnag,
            IConfiguration configuration
        ) : base(TimeSpan.FromMinutes(10), TimeSpan.Zero, client, bugsnag, configuration) {
            this.Configuration = Configuration;
        }

        public override async Task Run(object state) {
            var _db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);
            Console.WriteLine("Checking Delete Channel...");
            var coops = await _db.Coops.AsQueryable().Where(x => x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(3) < DateTimeOffset.Now && !x.DeletedChannel).ToListAsync();
            //var coops = await _db.Coops.AsQueryable().Where(x => x.CoopEnds.HasValue && x.CoopEnds.Value.AddDays(1) < DateTimeOffset.Now && !x.DeletedChannel).ToListAsync();


            coops.AddRange(await _db.Coops.AsQueryable().Where(x => x.Finished && !x.DeletedChannel && (x.CoopCompleted == null || x.CoopCompleted < DateTimeOffset.Now.AddDays(-2))).ToListAsync());
            //coops.AddRange(await _db.Coops.AsQueryable().Where(x => x.Finished && !x.DeletedChannel && (x.CoopCompleted == null || x.CoopCompleted < DateTimeOffset.Now.AddHours(-12))).ToListAsync());


            foreach(var coop in coops) {
                var coopChannel = (ITextChannel)_client.GetChannel(coop.DiscordChannelId);
                if(coopChannel == null) {
                    coopChannel = (ITextChannel)(await _client.Rest.GetChannelAsync(coop.DiscordChannelId));
                }
                if(coopChannel != null) {
                    try {
                        await coopChannel.DeleteAsync();
                    } catch(Exception) {

                    }
                    coop.DeletedChannel = true;
                    Console.WriteLine($"Deleting co-op channel for {coop.Name}");
                } else {
                    coop.DeletedChannel = true;
                    Console.WriteLine($"Unable to find co-op channel for {coop.Name}");
                }
                try {
                    await _db.SaveChangesAsync();
                } catch(Exception) {
                    await _db.SaveChangesAsync();
                }
            }


        }
    }
}
