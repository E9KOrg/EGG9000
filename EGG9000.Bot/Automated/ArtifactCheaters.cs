using Bugsnag.Payload;
using Discord;
using EGG9000.Bot.Common.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

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
            await RunFairnessScores(cancellationToken, true, false);
            //await RunCraftingLevelCheck(cancellationToken, true, false);
        }

        public async Task RunCraftingLevelCheck(CancellationToken cancellationToken, bool sendMessages = true, bool returnLevelSet = false) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            var dbusers = await _db.DBUsers.AsQueryable().Where(u => !u.TempDisabled).ToListAsync();
            var xpSet = new Dictionary<EggIncAccount, double>();

            foreach(var user in dbusers) {
                foreach(var account in user.EggIncAccounts.ToList()) {
                    if(cancellationToken.IsCancellationRequested) return;
                    if(account is null || account.Backup is null || account.Backup.CraftingXP == 0) continue;
                    xpSet.Add(account, account.Backup.CraftingXP);
                }
            }

            //Change this to actually relate how far above the average someone has to be to get flagged
            const double zScoreCutoff = 1.0;

            // Calculate the average score
            var sumXp = xpSet.Values.Sum();
            var averageXp = sumXp / xpSet.Where(s => s.Value > 0).Count();

            //Look at the top 50%
            xpSet = xpSet.Where(x => x.Value > averageXp).ToDictionary(pair => pair.Key, pair => pair.Value);
            sumXp = xpSet.Values.Sum();
            averageXp = sumXp / xpSet.Where(s => s.Value > 0).Count();

            // Calculate the standard deviation for Z-score calculation
            var sumSquaredDeviations = xpSet.Values.Sum(score => Math.Pow(score - averageXp, 2));
            var standardDeviation = Math.Sqrt(sumSquaredDeviations / xpSet.Count);

            // Calculate the Z-score for each account and find upper outliers
            var upperThreshold = averageXp + (zScoreCutoff * standardDeviation);
            var upperOutliers = xpSet
                .Where(pair => dbguilds.Any(g => g.Id == dbusers.FirstOrDefault(d => d.EggIncAccounts.Any(a => a.Name == pair.Key.Name)).GuildId))
                .Where(pair => !pair.Key.CraftingWarningSent)
                .Where(pair => !pair.Key.CraftingMarkedClean)
                .Where(pair => (pair.Value - averageXp) / standardDeviation > zScoreCutoff)
                .Select(pair => pair.Key)
                .ToList();

            if(sendMessages) {
                foreach(var outlier in upperOutliers) {
                    if(cancellationToken.IsCancellationRequested) return;
                    DBUser user;
                    if(string.IsNullOrEmpty(outlier.Name)) user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Id == outlier.Id));
                    else user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Name == outlier.Name));
                    var outlierScore = xpSet[outlier];

                    var guild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                    if(guild is null) continue;

                    var clientGuild = dbguilds.FirstOrDefault(x => x.Id == guild.Id);
                    if(clientGuild is null) continue;

                    var identifier = string.IsNullOrEmpty(outlier.Backup?.UserName) ? (string.IsNullOrEmpty(outlier.Name) ? outlier.Id : outlier.Name) : outlier.Backup.UserName;
#if DEV9002
                    var message = $"User `<@{user.DiscordId}>` may be cheating - the account `{identifier}` has `{outlierScore}` Crafting XP compared to the average of `{averageXp}`";
#else
                    var message = $"User <@{user.DiscordId}> may be cheating - the account `{identifier}` has `{outlierScore}` Crafting XP compared to the average of `{averageXp}`";
#endif

                    var response = await ChannelHelper.DetermineAndSend(_db, _client, clientGuild, guild, GuildChannelType.CheaterThread, new() { Text = message });

                    outlier.CraftingWarningSent = true;
                    user.UpdateAccounts();
                }
                await _db.SaveChangesAsync();
            }
        }

        public async Task<Dictionary<EggIncAccount, double>> RunFairnessScores(CancellationToken cancellationToken, bool sendMessages, bool returnScoreset) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync();
            var dbusers = await _db.DBUsers.AsQueryable().Where(u => !u.TempDisabled).ToListAsync();
            var scoreSet = new Dictionary<EggIncAccount, double>();

            foreach(var user in dbusers) {
                if(cancellationToken.IsCancellationRequested) return new();
                foreach(var account in user.EggIncAccounts.ToList()) {
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
                .Where(pair => dbguilds.Any(g => g.Id == dbusers.FirstOrDefault(d => d.EggIncAccounts.Any(a => a.Name == pair.Key.Name)).GuildId))
                .Where(pair => !pair.Key.AFSMarkedClean)
                .Where(pair => !pair.Key.AFSWarningSent)
                .Where(pair => (pair.Value - averageScore) / standardDeviation > zScoreCutoff)
                .Select(pair => pair.Key)
                .ToList();

            if(sendMessages) {
                foreach(var outlier in upperOutliers) {
                    if(cancellationToken.IsCancellationRequested) return new();
                    DBUser user;
                    if(string.IsNullOrEmpty(outlier.Name)) user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Id == outlier.Id));
                    else user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Name == outlier.Name));
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

                    var (B64, Config) = ArtifactHelpers.InventoryB64(outlier);
                    if(string.IsNullOrEmpty(B64)) {
                        var sendResponse = await ChannelHelper.DetermineAndSend(_db, _client, dbGuild, clientGuild, GuildChannelType.CheaterThread, new() {
                            Text = message
                        });
                    } else {
                        var image = new FileAttachment(new MemoryStream(Convert.FromBase64String(B64)), "Inventory.jpeg", "Inventory Image");
                        var sendResponse = await ChannelHelper.DetermineAndSend(_db, _client, dbGuild, clientGuild, GuildChannelType.CheaterThread, new() {
                            Text = message,
                            Embed = Commands.ArtifactCommands._inventoryEmbed(user, outlier),
                            File = image,
                            SendFile = true,
                        });
                        var baseUrl = sendResponse.Embeds.First().Image.ToString();
                        var formatIndex = baseUrl.IndexOf("&format", StringComparison.OrdinalIgnoreCase);
                        var imageUrl = formatIndex is int index && index != -1 ? baseUrl[..(index + "&format".Length)] : baseUrl;

                        await sendResponse.ModifyAsync(x => {
                            x.Content = message;
                            x.Embed = Commands.ArtifactCommands._inventoryEmbed(user, outlier, imageUrl);
                        });
                    }

                    outlier.AFSWarningSent = true;
                    user.UpdateAccounts();
                }
                await _db.SaveChangesAsync();
            }

            return returnScoreset ? scoreSet : new();
        }
    }
}
