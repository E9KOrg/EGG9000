using Discord;

using EGG9000.Common.Commands;
using EGG9000.Common.Contracts;
using EGG9000.Common.Contracts.Assignment.Diagnostics;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    // Admin-only, read-only assignment-parity validator. Unlike the DEV /test commands this is NOT
    // release-gated: it runs on the live PROD bot so parity can be checked against real data. It is fully
    // read-only (returns the report as a Discord attachment, writes no files). The continuous record lives
    // in the ShadowAssignmentDiffs table (written inline by AssignmentShadowRecorder during real runs);
    // this command is the on-demand sweep, including a historical mode to validate without waiting for
    // contract launches.
    public static class ParityCommands {
        public enum ParityScope { Active, Recent, All }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        [SlashCommand(Description = "Validate the new assignment engine vs frozen legacy (read-only)", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task Parity(FauxCommand command, ApplicationDbContext db,
            [SlashParam(Required = false, Description = "active (default) | recent (last 120d, all types) | all (every contract)")] ParityScope scope = ParityScope.Active) {

            await command.DeferAsync(ephemeral: true);

            var now = DateTimeOffset.UtcNow;
            var guildContracts = await db.GuildContracts
                .Include(x => x.Contract)
                .Where(x => !x.DeletedChannel)
                .ToListAsync();

            var withDetails = guildContracts.Where(gc => gc.Contract != null && gc.Contract.Details != null);

            // Active = live (non-expired). Recent = created in the last 120 days (covers every contract
            // type quickly). All = every non-deleted contract. Recent/All run both engines on CURRENT user
            // state against past contracts: the comparison is still valid because both engines see the same
            // inputs, so any divergence is pure engine logic (not historical state drift).
            IEnumerable<GuildContract> selected = scope switch {
                ParityScope.Active => withDetails.Where(gc => gc.Contract.GoodUntil.AddSeconds(gc.Contract.Details.LengthSeconds).AddDays(1) >= now),
                ParityScope.Recent => withDetails.Where(gc => gc.Contract.Created >= now.AddDays(-120)),
                _ => withDetails
            };

            var pairs = selected
                .GroupBy(gc => (gc.GuildID, gc.ContractID))
                .Select(g => g.First())
                .ToList();

            if(pairs.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No contracts found for scope '{scope}'."); });
                return;
            }

            var dbGuildById = (await db.Guilds.ToListAsync()).ToDictionary(g => g.Id);

            // Load each guild's users once and reuse across that guild's contracts (avoids re-loading
            // large user/backup sets per contract on the recent/all sweeps).
            var usersByGuild = new Dictionary<ulong, List<DBUser>>();
            async Task<List<DBUser>> UsersFor(ulong guildId) {
                if(!usersByGuild.TryGetValue(guildId, out var cached)) {
                    cached = await db.DBUsers.Where(u => u.GuildId == guildId).ToListAsync();
                    usersByGuild[guildId] = cached;
                }
                return cached;
            }

            var perContract = new List<AssignmentParityChecker.ParityReport>();
            var results = new List<(ulong guildId, AssignmentParityChecker.ParityReport report)>();
            var grandTotal = 0;
            var grandMatched = 0;
            var grandUnexpected = 0;
            var grandExpectedSeasonal = 0;

            foreach(var pair in pairs) {
                if(!dbGuildById.TryGetValue(pair.GuildID, out var dbGuild)) continue;
                var contract = pair.Contract;

                var users = await UsersFor(pair.GuildID);
                var coops = await db.Coops
                    .Include(c => c.UserCoopsXrefs)
                    .Where(c => c.ContractID == contract.ID && c.Created > now.AddDays(-60))
                    .ToListAsync();
                var csHistory = await db.UserCsHistoryEntries.Where(x => x.ContractIdentifier == contract.ID).ToListAsync();
                var (contractSeason, seasonProgresses) = await OrganizeCoops.LoadContractSeasonData(db, contract, users);

                var report = AssignmentParityChecker.Compare(users, contract, coops, dbGuild, contractSeason, seasonProgresses, csHistory);
                perContract.Add(report);

                grandTotal += report.Total;
                grandMatched += report.Matched;
                grandUnexpected += report.Mismatches.Count(m => !m.ExpectedSeasonalDeviation);
                grandExpectedSeasonal += report.Mismatches.Count(m => m.ExpectedSeasonalDeviation);

                results.Add((pair.GuildID, report));
            }

            // Report goes back as a Discord attachment - the container filesystem is read-only, and an
            // attachment is what the admin can actually open anyway. Oversized "all" reports drop the
            // expected-deviation rows (keep only the actionable unexpected mismatches).
            object ReportPayload(bool unexpectedOnly) => new {
                generatedAt = now,
                scope = scope.ToString(),
                contractsChecked = pairs.Count,
                grandTotal,
                grandMatched,
                grandUnexpected,
                grandExpectedSeasonal,
                truncatedToUnexpected = unexpectedOnly,
                contracts = results
                    .Where(x => !unexpectedOnly || x.report.Mismatches.Any(m => !m.ExpectedSeasonalDeviation))
                    .Select(x => new {
                        contractId = x.report.ContractId,
                        guildId = x.guildId,
                        total = x.report.Total,
                        matched = x.report.Matched,
                        mismatches = x.report.Mismatches
                            .Where(m => !unexpectedOnly || !m.ExpectedSeasonalDeviation)
                            .Select(m => new {
                                eggIncId = m.EggIncId,
                                discordId = m.DiscordId,
                                legacyAssigned = m.LegacyAssigned,
                                newAssigned = m.NewAssigned,
                                legacyReason = m.LegacyReason,
                                newReason = m.NewReason,
                                expectedSeasonalDeviation = m.ExpectedSeasonalDeviation
                            }).ToList()
                    }).ToList()
            };

            var json = JsonSerializer.Serialize(ReportPayload(false), JsonOptions);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            if(bytes.Length > 7_000_000) {
                json = JsonSerializer.Serialize(ReportPayload(true), JsonOptions);
                bytes = System.Text.Encoding.UTF8.GetBytes(json);
            }

            var builder = new EmbedBuilder()
                .WithTitle($"Assignment Parity - scope: {scope}")
                .WithColor(grandUnexpected > 0 ? Color.Red : Color.Green)
                .WithDescription(
                    $"Contracts checked: **{pairs.Count}**\n" +
                    $"Accounts: **{grandMatched}/{grandTotal}** matched\n" +
                    $"Unexpected mismatches: **{grandUnexpected}**\n" +
                    $"Expected seasonal deviations: **{grandExpectedSeasonal}**\n" +
                    "Full report attached.");

            var lines = perContract
                .OrderByDescending(r => r.Mismatches.Count(m => !m.ExpectedSeasonalDeviation))
                .Take(20)
                .Select(r => {
                    var unexpected = r.Mismatches.Count(m => !m.ExpectedSeasonalDeviation);
                    var mark = unexpected > 0 ? "🔴" : "🟢";
                    return $"{mark} `{r.ContractId}` {r.Matched}/{r.Total}" + (unexpected > 0 ? $" - **{unexpected}** unexpected" : "");
                })
                .ToList();

            if(lines.Count > 0)
                builder.AddField($"Per-contract ({perContract.Count} total, top 20 by unexpected)", string.Join("\n", lines));

            try {
                var attachment = new FileAttachment(new MemoryStream(bytes), $"parity-{scope.ToString().ToLower()}-{now.ToUnixTimeSeconds()}.json");
                await command.RespondWithFileAsync(attachment, text: "", embed: builder.Build(), ephemeral: true);
            } catch {
                // Never let report delivery break the result - fall back to the summary embed.
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = builder.Build(); });
            }
        }
    }
}
