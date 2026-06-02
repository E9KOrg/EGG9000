using Discord;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Common.Contracts;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using static EGG9000.Common.Helpers.CreateCoopsV2;

using Humanizer;

using MassTransit.Initializers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using static EGG9000.Common.Helpers.Prefarm;
using System.Collections.Concurrent;
using MassTransit.Internals;
using Microsoft.Extensions.Caching.Memory;
using static Ei.Contract.Types;
using EGG9000.Bot.Services;
using EGG9000.Common.EggIncAPI;
using MassTransit;
using EGG9000.Common.Factories;

namespace EGG9000.Bot.Automated.Coops {
    public class CreateCoopViaAPI(IServiceProvider provider, BotLogger botLogger) : _UpdaterBase<CreateCoopViaAPI>(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(0), provider) {
        private readonly Dictionary<string, int> CoopsTimeoutCounter = new();
        private readonly BotLogger _botLogger = botLogger;

        public async override Task Run(object state, CancellationToken cancellationToken) {
            ulong.TryParse(_configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);

            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();


            List<Coop> allCoops;

            var dbguilds = _db.CachedGuilds.ToList();

            Dictionary<(ulong guildid, string contractid, ulong bggroup), (int successes, int failures, bool changed)> guildStats = new();

            while(
                (allCoops = await _db.Coops.Include(c => c.Contract).AsQueryable().Where(x => x.Status == CoopStatusEnum.WaitingOnCreation).OrderByDescending(x => x.MaxUsers).ToListAsync(CancellationToken.None))
                .Count > 0) {
                if(cancellationToken.IsCancellationRequested) return;

                var guildIDs = allCoops.Select(x => x.GuildId).Distinct().ToList();

                var coops = new List<Coop>();

                while(allCoops.Count > 0 && coops.Count < 20) {
                    foreach(var guildID in guildIDs) {
                        if(allCoops.Any(x => x.GuildId == guildID && x.League < (uint)Ei.Contract.Types.PlayerGrade.GradeAaa)) {
                            var lowgradeCoop = allCoops.FirstOrDefault(x => x.GuildId == guildID && x.League < (uint)Ei.Contract.Types.PlayerGrade.GradeAaa);
                            if(lowgradeCoop != null) {
                                coops.Add(lowgradeCoop);
                                allCoops.Remove(lowgradeCoop);
                            }
                        }
                        var coop = allCoops.FirstOrDefault(x => x.GuildId == guildID);
                        if(coop != null) {
                            coops.Add(coop);
                            allCoops.Remove(coop);
                        }
                    }
                }

                if(coops.Count > 5) _coopsBeingCreatedService.SetCoopsAreBeingCreated(true);


                await Parallel.ForEachAsync(
                    coops,
                    new ParallelOptions {
                        MaxDegreeOfParallelism = 5,
                        CancellationToken = cancellationToken
                    },
                    async (coop, ct) => {
                        if(cancellationToken.IsCancellationRequested) return;
                        var timings = new TimingsFactory(_logger);
                        timings.Start();
                        StillAlive();

                        try {
                            var secondsRemaining = Math.Max(coop.Contract.Details.LengthSeconds, TimeSpan.FromDays(1.6).TotalSeconds);

                            timings.Set("Setup");
                            var creator = EggIncApi.CoopCreatorIds.FirstOrDefault(x => x.EggIncId == coop.CreatorID);
                            await CreateCoopViaApi(coop.ContractID, (PlayerGrade)coop.League, coop.Name, secondsRemaining, coop.CreatorID, coop.AnyLeague, kickCreator: creator == default, timings: timings);

                            timings.Set("Coop API Call");



                            coop.Status = CoopStatusEnum.WaitingOnThread;
                            using var writeScope = _provider.CreateScope();
                            var writeDb = writeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            await writeDb.Coops.Where(c => c.Id == coop.Id).ExecuteUpdateAsync(s => s
                                .SetProperty(c => c.Status, coop.Status));
                            writeDb.Dispose();
                            timings.Set("Updated db");

                            IncrementGuildStats(guildStats, coop.GuildId, coop.ContractID, coop.Group, true);
                            var timingsReulsts = timings.Finished();

                            _logger.LogInformation("Timings for creating coop via API: {timings}", string.Join(", ", timingsReulsts.Select(x => $"{x.name}: {x.time.TotalSeconds}s")));
                        } catch(Exception ex) {
                            IncrementGuildStats(guildStats, coop.GuildId, coop.ContractID, coop.Group, false);
                            _logger.LogError(ex, "Error Creating Co-op via API {coop}", coop.Name);

                        }
                    });

                foreach(var guildStat in guildStats.Where(x => x.Value.changed)) {
                    guildStats[guildStat.Key] = (guildStat.Value.successes, guildStat.Value.failures, false);
                    //await _botLogger.Log($"{guildStat.Value.successes} of {guildStat.Value.successes + guildStat.Value.failures} co-ops started ({guildStat.Value.failures} failed)", guildStat.Key);
                    await _botLogger.UpdateBoardingGroup((int)guildStat.Key.bggroup, guildStat.Key.contractid, guildStat.Key.guildid, null, startedCount: guildStat.Value.successes, null);
                }
            }
            _coopsBeingCreatedService.SetCoopsAreBeingCreated(false);
        }

        private void IncrementGuildStats(Dictionary<(ulong guildid, string contractid, ulong bggroup), (int successes, int failures, bool changed)> guildStats, ulong guildId, string contractId, ulong bgGroup, bool success) {
            lock(guildStats) {
                var key = (guildId, contractId, bgGroup);
                if(!guildStats.ContainsKey(key)) {
                    guildStats[key] = (0, 0, false);
                }
                if(success) {
                    guildStats[key] = (guildStats[key].successes + 1, guildStats[key].failures, true);
                } else {
                    guildStats[key] = (guildStats[key].successes, guildStats[key].failures + 1, true);
                }
            }
        }
    }
}
