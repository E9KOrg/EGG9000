using Discord;
using EGG9000.Bot.Commands.Informational;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class ArtifactCheaters(IServiceProvider provider) : _UpdaterBase<ArtifactCheaters>(interval, delay, provider) {
        private static readonly TimeSpan delay = TimeSpan.FromMinutes(0);
#if DEBUG
        private static readonly TimeSpan interval = TimeSpan.FromMinutes(1);
#else
        public static readonly TimeSpan interval = TimeSpan.FromMinutes(30);
#endif

        public async override Task Run(object state, CancellationToken cancellationToken) {
            await RunFairnessScores(sendMessages: true, returnScoreSet: false, cancellationToken);
        }

        public async Task<Dictionary<EggIncAccount, double>> RunFairnessScores(bool sendMessages, bool returnScoreSet, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);
            var dbusers = await _db.DBUsers.AsQueryable().Where(u => !u.TempDisabled).ToListAsync(CancellationToken.None);
            var scoreSet = new Dictionary<EggIncAccount, double>();
            var accountOwner = new Dictionary<EggIncAccount, DBUser>();

            foreach(var user in dbusers) {
                if(cancellationToken.IsCancellationRequested) return [];
                foreach(var account in user.EggIncAccounts.ToList()) {
                    var score = (double)ArtifactHelpers.GetArtifactFairnessScore(account.Backup?.ArtifactHall ?? null);
                    if(account is null || score is 0) continue;
                    scoreSet.Add(account, score);
                    accountOwner[account] = user;
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
                .Where(pair => dbguilds.Any(g => g.Id == accountOwner[pair.Key].GuildId))
                .Where(pair => !pair.Key.AFSMarkedClean)
                .Where(pair => !pair.Key.AFSWarningSent)
                .Where(pair => (pair.Value - averageScore) / standardDeviation > zScoreCutoff)
                .Select(pair => pair.Key)
                .ToList();

            if(sendMessages) {
                foreach(var outlier in upperOutliers) {
                    if(cancellationToken.IsCancellationRequested) return [];
                    var user = accountOwner[outlier];
                    var outlierScore = scoreSet[outlier];

                    var clientGuild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                    if(clientGuild is null) continue;

                    var dbGuild = dbguilds.FirstOrDefault(x => x.Id == clientGuild.Id);
                    if(dbGuild is null) continue;

                    var doesCheaterChannelExist = ChannelHelper.DetermineChannelType(dbGuild, clientGuild, GuildChannelType.CheaterThread);

                    //Only run through if the channel exists
                    if(doesCheaterChannelExist is null) continue;

                    var identifier = string.IsNullOrEmpty(outlier.Backup?.UserName) ? (string.IsNullOrEmpty(outlier.Name) ? outlier.Id : outlier.Name) : outlier.Backup.UserName;
#if DEV9002
                    var message = $"User `<@{user.DiscordId}>` may be using cheated artifacts - the account `{identifier}` has an AFS of `{outlierScore}` compared to the average of `{averageScore}`";
#else
                    var message = $"User <@{user.DiscordId}> may be using cheated artifacts - the account `{identifier}` has an AFS of `{outlierScore}` compared to the average of `{averageScore}`";
#endif

                    var (B64, Config) = await ArtifactHelpers.InventoryB64(outlier);
                    if(string.IsNullOrEmpty(B64)) {
                        var sendResponse = await ChannelHelper.DetermineAndSend(_client.Gateway, dbGuild, GuildChannelType.CheaterThread, new() {
                            Text = message
                        });
                    } else {
                        var image = new FileAttachment(new MemoryStream(Convert.FromBase64String(B64)), "Inventory.jpeg", "Inventory Image");
                        var sendResponse = await ChannelHelper.DetermineAndSend(_client.Gateway, dbGuild, GuildChannelType.CheaterThread, new() {
                            Text = message,
                            Embed = ArtifactCommands._inventoryEmbed(user, outlier),
                            File = image,
                            SendFile = true,
                        });
                        var imageUrl = ArtifactCommands.TrimImageUrl(sendResponse.Embeds.First().Image.ToString());

                        await sendResponse.ModifyAsync(x => {
                            x.Content = message;
                            x.Embed = ArtifactCommands._inventoryEmbed(user, outlier, imageUrl);
                        });
                    }

                    outlier.AFSWarningSent = true;
                    user.UpdateAccounts();
                }
                await _db.SaveChangesAsync(CancellationToken.None);
            }

            return returnScoreSet ? scoreSet : [];
        }
    }
}
