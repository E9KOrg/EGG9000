using Discord;
using EGG9000.Bot.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class ArtifactCheaters(IServiceProvider provider) : _UpdaterBase<ArtifactCheaters>(interval, delay, provider) {
        private static readonly TimeSpan delay = TimeSpan.FromMinutes(0);
        private static readonly TimeSpan interval = BuildConfig.IsDebug ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(30);

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

            const double zScoreCutoff = 1.0;

            var sumXp = xpSet.Values.Sum();
            var averageXp = sumXp / xpSet.Count(s => s.Value > 0);

            xpSet = xpSet.Where(x => x.Value > averageXp).ToDictionary(pair => pair.Key, pair => pair.Value);
            sumXp = xpSet.Values.Sum();
            averageXp = sumXp / xpSet.Where(s => s.Value > 0).Count();

            var sumSquaredDeviations = xpSet.Values.Sum(score => Math.Pow(score - averageXp, 2));
            var standardDeviation = Math.Sqrt(sumSquaredDeviations / xpSet.Count);

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
                    await WaitOnCoopsBeingCreated(cancellationToken);

                    DBUser user;
                    if(string.IsNullOrEmpty(outlier.Name)) user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Id == outlier.Id));
                    else user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Name == outlier.Name));
                    var outlierScore = xpSet[outlier];

                    var dbGuild = dbguilds.FirstOrDefault(x => x.Id == user.GuildId);
                    if(dbGuild is null) continue;

                    // GuildId can be stale when a user leaves Discord without being unassigned; skip the
                    // ping when the roster is complete and the user is no longer a member.
                    var clientGuild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                    if(clientGuild is not null) {
                        await clientGuild.DownloadUsersAsync();
                        if(clientGuild.HasAllMembers && clientGuild.GetUser(user.DiscordId) is null) continue;
                    }

                    var identifier = string.IsNullOrEmpty(outlier.Backup?.UserName) ? (string.IsNullOrEmpty(outlier.Name) ? outlier.Id : outlier.Name) : outlier.Backup.UserName;
                    var message = BuildConfig.IsDev9002
                        ? $"User `<@{user.DiscordId}>` may be cheating - the account `{identifier}` has `{outlierScore}` Crafting XP compared to the average of `{averageXp}`"
                        : $"User <@{user.DiscordId}> may be cheating - the account `{identifier}` has `{outlierScore}` Crafting XP compared to the average of `{averageXp}`";

                    var response = await ChannelHelper.DetermineAndSend(_client.Gateway, dbGuild, GuildChannelType.CheaterThread, new() { Text = message });

                    outlier.CraftingWarningSent = true;
                    user.UpdateAccounts();
                }
                await _db.SaveChangesAsync();
            }
        }

        // An account's legendary count vs. what its actual ship launches/crafts predict (LLC.LLCPercent)
        // is the strongest single cheat signal since it's grounded in that account's own play history,
        // not a population comparison. AFS (relative artifact-rarity stacking) is corroborating only -
        // it catches inventory anomalies LLC doesn't model (e.g. hoarded rares with no legendary craft),
        // but alone it flags legit whales with huge launch counts, so it never fires without LLC agreeing
        // something is at least somewhat off too.
        private const int LLCPercentHardCutoff = 50;
        private const int LLCPercentSoftCutoff = 15;
        private const double AFSZScoreCutoff = 1.0;

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var _cache = _provider.CreateScope().ServiceProvider.GetRequiredService<IMemoryCache>();
            var dbguilds = await _db.Guilds.AsQueryable().ToListAsync(CancellationToken.None);
            var dbusers = await _db.DBUsers.AsQueryable().Where(u => !u.TempDisabled).ToListAsync(CancellationToken.None);
            var scoreSet = new Dictionary<EggIncAccount, double>();
            var candidateAccounts = new List<EggIncAccount>();

            foreach(var user in dbusers) {
                if(cancellationToken.IsCancellationRequested) return;
                foreach(var account in user.EggIncAccounts.ToList()) {
                    if(account?.Backup?.ArtifactHall is null) continue;
                    var score = ArtifactHelpers.GetArtifactFairnessScore(account.Backup.ArtifactHall);
                    if(score is 0) continue;
                    scoreSet.Add(account, score);
                    candidateAccounts.Add(account);
                }
            }

            var sumScores = scoreSet.Values.Sum();
            var averageScore = sumScores / scoreSet.Where(s => s.Value > 0).Count();

            var sumSquaredDeviations = scoreSet.Values.Sum(score => Math.Pow(score - averageScore, 2));
            var standardDeviation = Math.Sqrt(sumSquaredDeviations / scoreSet.Count);

            var afsZScores = scoreSet.ToDictionary(pair => pair.Key, pair => (pair.Value - averageScore) / standardDeviation);

            var shipCoefficientTable = await ArtifactHelpers.GetShipDataTable(_cache, _logger);
            var llcResults = new Dictionary<EggIncAccount, ArtifactHelpers.LegendaryLuckResult>();
            if(shipCoefficientTable is not null) {
                // Mirror UpdateBackups' throttling - FirstContact is a live API call per account, so cap
                // concurrency instead of firing one request per account at once.
                var throttler = new SemaphoreSlim(8);
                var tasks = candidateAccounts
                    .Where(a => afsZScores.TryGetValue(a, out var z) && z > AFSZScoreCutoff / 2)
                    .Select(async account => {
                        await throttler.WaitAsync(cancellationToken);
                        try {
                            var firstContact = await EggIncApi.FirstContact(account.Id, _logger);
                            if(firstContact?.Backup?.ArtifactsDb?.MissionArchive is null) return;
                            var llc = ArtifactHelpers.GetLegendaryLuckCoefficient(account, firstContact.Backup, shipCoefficientTable);
                            lock(llcResults) llcResults[account] = llc;
                        } catch(Exception ex) {
                            _logger.LogError(ex, "Error computing LLC for account {accountId}", account.Id);
                        } finally {
                            throttler.Release();
                        }
                    });
                await Task.WhenAll(tasks);
            }

            // Informed combination: a lone-standing implausible legendary excess is damning on its own.
            // A merely elevated one only counts alongside a corroborating AFS outlier, so a legit whale
            // with a big launch history (which pushes AFS up too, but LLC stays near zero) never flags.
            bool IsFlagged(EggIncAccount account) {
                var hasLlc = llcResults.TryGetValue(account, out var llc);
                var hasAfs = afsZScores.TryGetValue(account, out var afsZ);
                if(hasLlc && llc.LLCPercent >= LLCPercentHardCutoff) return true;
                if(hasLlc && hasAfs && llc.LLCPercent >= LLCPercentSoftCutoff && afsZ > AFSZScoreCutoff) return true;
                return false;
            }

            var upperOutliers = scoreSet
                .Where(pair => !pair.Key.AFSMarkedClean
                    && !pair.Key.AFSWarningSent
                    && IsFlagged(pair.Key)
                    && dbguilds.Any(g => g.Id == dbusers.FirstOrDefault(d => d.EggIncAccounts.Any(a => a.Name == pair.Key.Name)).GuildId))
                .Select(pair => pair.Key)
                .ToList();

            foreach(var outlier in upperOutliers) {
                if(cancellationToken.IsCancellationRequested) return;
                DBUser user;
                if(string.IsNullOrEmpty(outlier.Name)) user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Id == outlier.Id));
                else user = dbusers.FirstOrDefault(u => u.EggIncAccounts.Any(a => a.Name == outlier.Name));
                var outlierScore = scoreSet[outlier];

                var clientGuild = _client.Guilds.FirstOrDefault(x => x.Id == user.GuildId);
                if(clientGuild is null) continue;

                // GuildId can be stale when a user leaves Discord without being unassigned; skip the
                // ping when the roster is complete and the user is no longer a member.
                await clientGuild.DownloadUsersAsync();
                if(clientGuild.HasAllMembers && clientGuild.GetUser(user.DiscordId) is null) continue;

                var dbGuild = dbguilds.FirstOrDefault(x => x.Id == clientGuild.Id);
                if(dbGuild is null) continue;

                var doesCheaterChannelExist = ChannelHelper.DetermineChannelType(dbGuild, clientGuild, GuildChannelType.CheaterThread);

                if(doesCheaterChannelExist is null) continue;

                var identifier = string.IsNullOrEmpty(outlier.Backup?.UserName) ? (string.IsNullOrEmpty(outlier.Name) ? outlier.Id : outlier.Name) : outlier.Backup.UserName;
                var llcSuffix = llcResults.TryGetValue(outlier, out var outlierLlc)
                    ? $", LLC `{outlierLlc.LLC:f2}` (`{outlierLlc.LLCPercent}%` above expected legendaries)"
                    : "";
                var message = BuildConfig.IsDev9002
                    ? $"User `<@{user.DiscordId}>` may be using cheated artifacts - the account `{identifier}` has an AFS of `{outlierScore}` compared to the average of `{averageScore}`{llcSuffix}"
                    : $"User <@{user.DiscordId}> may be using cheated artifacts - the account `{identifier}` has an AFS of `{outlierScore}` compared to the average of `{averageScore}`{llcSuffix}";

                var (B64, Config) = await ArtifactHelpers.InventoryB64(outlier);
                if(string.IsNullOrEmpty(B64) || B64.StartsWith("$ERROR$:")) {
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
    }
}
