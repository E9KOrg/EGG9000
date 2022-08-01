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
    public class CoopReorder : _UpdaterBase<CoopReorder> {
        public CoopReorder(
            IServiceProvider provider
        ) : base(TimeSpan.FromMinutes(10), TimeSpan.Zero, provider) {
        }
        public override async Task Run(object state, CancellationToken cancellationToken) {
            try
            {
                var _db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
                var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
                foreach (var dbguild in dbguilds)
                {
                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == dbguild.Id);
                    if (guild == null)
                        continue;

                    var categories = await _client.GetAllCoopCategories(guild);

                    var coops = await _db.Coops.Include(x => x.Contract).Where(x => !x.Finished && !x.DeletedChannel && x.GuildId == guild.Id && (x.OverflowGuildId == 0 || x.OverflowGuildId == guild.Id)).ToListAsync();

                    await SortCoops(coops, categories, guild);

                    foreach(var overflowId in dbguild.OverflowServers) {
                        var overflowGuild = _client.Guilds.FirstOrDefault(x => x.Id == overflowId);
                        if(guild == null) {
                            Console.WriteLine($"Missing overflow guild for {guild.Name}, overflowId = {overflowId}");
                            continue;
                        }
                        var overflowCategories = await _client.GetAllCoopCategories(overflowGuild);
                        var overflowCoops = await _db.Coops.Include(x => x.Contract).Where(x => !x.Finished && !x.DeletedChannel && x.GuildId == guild.Id && x.OverflowGuildId == overflowId).ToListAsync();
                        await SortCoops(overflowCoops, overflowCategories, overflowGuild);
                    }
                }
                Console.WriteLine("All co-ops consolidated into categories");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to sort co-ops {e.Message}");
                _bugsnag.Notify(e);
            }
        }

        private async Task SortCoops(List<Coop> coops, List<SocketCategoryChannel> categories, SocketGuild guild) {
            Console.WriteLine($"Sorting Co-ops for {guild.Name}");
            coops = coops.OrderBy(x =>
            {
                //x.CoopEnds
                var targetAmount = x.Contract.Details.GoalSets.Count > 0 ? x.Contract.Details.GoalSets[(int?)x.League ?? 0].Goals.Last().TargetAmount : x.Contract.Details.Goals.Last().TargetAmount;
                var totalAmount = x.LastStatusUpdate?.Participants.Sum(x => x.AmountWithOfflineIgnoreSilo()) ?? 0;
                var remainingAmount = targetAmount - totalAmount;
                var totalRate = x.LastStatusUpdate?.Participants.Sum(x => x.ContributionRate) ?? 0;
                var timeRemaining = Prefarm.GetTimeRemainingValue(targetAmount, totalRate, totalAmount);

                return Math.Min(x.LastStatusUpdate?.SecondsRemaining ?? double.MaxValue, timeRemaining.TotalSeconds);
            }
).ToList();

            var groupCount = Math.Ceiling(coops.Count / 50m);
            var lastCount = coops.Count % 50;

            var currentCounts = categories.Select(x => guild.TextChannels.Where(y => y.CategoryId == x.Id).Count()).ToArray();

            var currentIndex = 0;
            var categoryIndex = 0;

            var coopSorts = new List<CoopSort>();

            foreach(var coop in coops.Where(x => x.DiscordChannelId > 0)) {
                var channel = guild.TextChannels.FirstOrDefault(x => x.Id == coop.DiscordChannelId);
                if(channel == null) {
                    Console.WriteLine($"Unable to find channel for {coop.Name}");
                    continue;
                }
                var currentCategory = categories.FirstOrDefault(x => x.Id == channel.Category.Id);
                if(currentCategory == null)
                    continue;
                if(++currentIndex >= 50) {
                    currentIndex = 0;
                    categoryIndex++;
                }
                var targetCategory = (ICategoryChannel)categories[categoryIndex];
                coopSorts.Add(new CoopSort {
                    NewPosition = currentIndex,
                    CurrentPosition = channel.Position,
                    Channel = channel,
                    CurrentCategory = channel.Category,
                    TargetCategory = targetCategory,
                    CurrentCategoryIndex = categories.IndexOf(currentCategory),
                    TargetCategoryIndex = categories.IndexOf(categories.First(x => x.Id == targetCategory.Id)),
                    NeedsMove = targetCategory != channel.Category,
                    NeedsReorder = targetCategory != channel.Category || currentIndex != channel.Position
                });
            }

            //Console.WriteLine("CoopSorts count " + coopSorts.Count);

            var currentTry = 0;
            while(coopSorts.Any(x => x.NeedsMove)) {
                //Console.WriteLine($"Sort Round {currentTry + 1}");
                if(currentTry++ > 1000)
                    break;
                foreach(var coopSort in coopSorts.Where(x => x.NeedsMove)) {
                    if(currentCounts[coopSort.TargetCategoryIndex] < 50) {
                        await coopSort.Channel.ModifyAsync(channel => channel.CategoryId = coopSort.TargetCategory.Id);
                        currentCounts[coopSort.TargetCategoryIndex]++;
                        currentCounts[coopSort.CurrentCategoryIndex]--;
                        coopSort.NeedsMove = false;
                        await Task.Delay(500);
                    }
                }
            }

            for(int i = coopSorts.Count - 1; i >= 0; i--) {
                var coopSort = coopSorts[i];
                if(coopSort.NeedsReorder) {
                    await coopSort.Channel.ModifyAsync(x => x.Position = coopSort.NewPosition);
                    await Task.Delay(500);
                }
            }

        }

        private class CoopSort {
            public int CurrentPosition { get; set; }
            public int NewPosition { get; set; }
            public ICategoryChannel CurrentCategory { get; set; }
            public ICategoryChannel TargetCategory { get; set; }
            public int CurrentCategoryIndex { get; set; }
            public int TargetCategoryIndex { get; set; }
            public SocketTextChannel Channel { get; set; }
            public Coop Coop { get; set; }
            public bool NeedsMove { get; set; }
            public bool NeedsReorder { get; set; }
            //public SocketGuildChannel CoopChannel { get; set; }
        }
    }
}
