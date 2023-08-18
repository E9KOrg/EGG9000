using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class ArtifactCheaters : _UpdaterBase<ArtifactCheaters> {
        private static TimeSpan delay = TimeSpan.FromMinutes(0);
#if DEBUG
        private static TimeSpan interval = TimeSpan.FromMinutes(1);
#else
        public static TimeSpan interval = TimeSpan.FromMinutes(30);
#endif
        public ArtifactCheaters(IServiceProvider provider) : base(interval, delay, provider) { }

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();

            var dbusers = await _db.DBUsers.AsQueryable().Where(u => !u.TempDisabled).ToListAsync();

            var scoreSet = new Dictionary<EggIncAccount, double>();

            foreach(var user in dbusers) {
                foreach(var account in user.EggIncAccounts) {
                    var score = (double)ArtifactHelpers.GetArtifactFairnessScore(account.Backup?.ArtifactHall ?? null);
                    if(account is null || score is 0) continue;
                    scoreSet.Add(account, score);
                }
            }

            //Change this to actually relate how far above the average someone has to be to get flagged
            const double zScoreCutoff = 1.0;

            // Calculate the average score
            var sumScores = scoreSet.Values.Sum();
            var averageScore = sumScores / scoreSet.Where(s => s.Value > 0).Count();

            // Calculate the standard deviation for Z-score calculation
            var sumSquaredDeviations = scoreSet.Values.Sum(score => Math.Pow(score - averageScore, 2));
            var standardDeviation = Math.Sqrt(sumSquaredDeviations / scoreSet.Count);

            // Calculate the Z-score for each account and find upper outliers
            var upperThreshold = averageScore + (zScoreCutoff * standardDeviation);
            var upperOutliers = scoreSet
                .Where(pair => dbguilds.Any(g => g.Id == dbusers.FirstOrDefault(d => d.EggIncAccounts.Any(a => a.Id == pair.Key.Id)).GuildId))
                .Where(pair => (pair.Value - averageScore) / standardDeviation > zScoreCutoff)
                .Select(pair => pair.Key)
                .ToList();

            foreach(var outlier in upperOutliers) {
                var user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Id == outlier.Id));
                var outlierScore = scoreSet[outlier];

                var guild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                if(guild is null) continue;

                var clientGuild = dbguilds.FirstOrDefault(x => x.Id == guild.Id);
                if(clientGuild is null) continue;

                var threadobj = clientGuild.ChannelDetails.FirstOrDefault(x => x.ChannelType == GuildChannelType.ArtifactCheaterThread);
                if(threadobj is null) continue;

                var thread = guild.GetThreadChannel(threadobj.Id);
                if(thread is null) continue;

                var messageContent = $"User <@{user.DiscordId}> is likely using cheated artifacts - the account `{outlier.Backup?.UserName}` has an AFS of `{outlierScore}` compared to the average of `{averageScore}`";
                var messages = await thread.GetMessagesAsync(100).FlattenAsync();

                if(!messages.Any(m => m.Content.ToString().Contains(outlier.Backup?.UserName) && m.Content.ToString().Contains(user.DiscordId.ToString()))) await thread.SendMessageAsync(messageContent);
                else {
                    _logger.LogInformation("Skipping sending thread message for {user} - {outlier} due to it already existing", user.DiscordUsername, outlier.Backup?.UserName);
                }
            }

        }
    }
}
