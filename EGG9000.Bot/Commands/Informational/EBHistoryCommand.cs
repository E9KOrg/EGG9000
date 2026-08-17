using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.Discord.Paging;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.CommonTypes.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands.Informational {
    public class EBHistoryPager(IReadOnlyList<string> lines, int page, string preamble, string headerRow, Guid dbUserId, int accountIndex) : TextListPager(lines, page, 3500) {
        protected override string Title => "EB History";
        protected override Color EmbedColor => Color.DarkGreen;
        protected override string Preamble => preamble;
        protected override string WrapBody(string body) => $"```\n{headerRow}\n{body}```";
        protected override string CustomIdPrefix => "EBHistoryPage";
        protected override string KeySuffix => $"{dbUserId},{accountIndex}";

        public static (Guid DbUserId, int AccountIndex, int Page) ParseCustomId(string data) {
            var parts = data.Split(",");
            return (Guid.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
        }
    }

    public partial class EBHistoryModule(IDbContextFactory<ApplicationDbContext> dbFactory) : Interactions.E9KModuleBase(dbFactory) {

        private class TextHistoryEntry(DateOnly entryDate, string ebString, string roleString, TextHistoryEntry lastEntry = null) {
            public DateOnly EntryDate { get; set; } = entryDate;
            public string EBString { get; set; } = ebString;
            public string RankString { get; set; } = roleString;
            public TextHistoryEntry LastEntry { get; set; } = lastEntry;

            private int EBPadding = 0;
            private int RankPadding = 0;

            public void MutateStrings(int ebLength, int rankLength) {
                if(EBString.Length < ebLength) EBPadding = ebLength - EBString.Length;
                if(RankString.Length < rankLength) RankPadding = rankLength - RankString.Length;
            }

            private string GetDateDifference() {
                if(LastEntry == null) return "";

                var lastEntryDateTime = LastEntry.EntryDate.ToDateTime(new());
                var currentEntryDateTime = EntryDate.ToDateTime(new());
                var offset = lastEntryDateTime - currentEntryDateTime;

                return "  " + (offset.Days * -1) + "d";
            }

            public override string ToString() {
                return $"{EntryDate:yyyy-MM-dd}  {EBString}%{(EBPadding <= 0 ? "" : new string(' ', EBPadding))}  {RankString}{(RankPadding <= 0 ? "" : new string(' ', RankPadding))}{GetDateDifference()}";
            }

        }

        private static (List<string> Lines, string HeaderRow) BuildEntries(List<UserSnapShot> snapshots) {
            var entries = new List<TextHistoryEntry>();
            var iterRank = "";
            foreach(var ssEntry in snapshots) {
                var rank = SIPrefix.GetPrefixFromEB(ssEntry.EarningsBonus).RankWithSubRank.Replace("farmer", "");
                if(rank == iterRank) continue;

                iterRank = rank;
                entries.Add(new(
                    DateOnly.FromDateTime(ssEntry.Date),
                    ssEntry.EarningsBonus.ToEggStringD(3),
                    rank,
                    entries.Count == 0 ? null : entries.Last()
                ));
            }

            var longestEBLength = entries.Max(e => e.EBString.Length);
            var longestRankLength = entries.Max(e => e.RankString.Length);
            entries.ForEach(e => e.MutateStrings(longestEBLength, longestRankLength));

            var headerRow = $"Date        EB{new string(' ', longestEBLength - 2)}   Rank{new string(' ', longestRankLength - 2)}";
            return (entries.Select(e => e.ToString()).ToList(), headerRow);
        }

        [SlashCommand("ebhistory", "View key points in your EB history")]
        public async Task EBHistory([Autocomplete(typeof(PersonalUserAccountAutoComplete))][Summary("useraccount")] string useraccount, [Summary("showinchannel")] bool showinchannel = false) {
            await Context.Interaction.DeferAsync(ephemeral: !showinchannel);
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) {
                if(MyRegex().IsMatch(useraccount)) {
                    await Context.Interaction.DeleteOriginalResponseAsync();
                    await Context.Channel.SendMessageAsync(embed: EmbedError($"{Context.User.Mention} - Please select an account from the list, instead of typing an input.\n\n**(Command use deleted to hide your EID)**."));
                } else {
                    await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); });
                }
                return;
            }
            if(dbuser is null) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID {userid}"); }); return; }

            if(dbuser.DiscordId != Context.User.Id) {
                await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Stop trying to run commands on others' accounts."); });
                return;
            }

            var accountIndex = int.Parse(useraccount.Split("|")[1]);
            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[accountIndex]; } catch(Exception) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            var snapshots = await Db.UserSnapShots.AsQueryable().Where(x => x.UserId == dbuser.Id && x.EggIncID == account.Id).ToListAsync();

            if(!snapshots.Any()) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No EB history for {userid} ({account.Backup?.UserName ?? account.Name}) could not be found"); }); return; }

            var (lines, headerRow) = BuildEntries(snapshots);
            var preamble = $"{Context.User.Mention} - {account.Backup?.UserName ?? account.Name}'s Earnings Boost rank history, with a first entry from {DiscordHelpers.TimeStamper(snapshots.First().Date)}.";

            var pager = new EBHistoryPager(lines, 0, preamble, headerRow, dbuser.Id, accountIndex);
            await pager.SendAsync(Context.Interaction);
        }

        [ComponentInteraction("EBHistoryPage:*", ignoreGroupNames: true)]
        public async Task EBHistoryPage(string data) {
            var component = (SocketMessageComponent)Context.Interaction;
            var (dbUserId, accountIndex, page) = EBHistoryPager.ParseCustomId(data);

            var dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == dbUserId);
            if(dbuser is null || accountIndex < 0 || accountIndex >= dbuser.EggIncAccounts.Count) return;
            if(component.User.Id != dbuser.DiscordId) { await Pager.RejectNonInvokerAsync(component); return; }

            var account = dbuser.EggIncAccounts[accountIndex];
            var snapshots = await Db.UserSnapShots.AsQueryable().Where(x => x.UserId == dbuser.Id && x.EggIncID == account.Id).ToListAsync();
            if(snapshots.Count == 0) return;

            var (lines, headerRow) = BuildEntries(snapshots);
            var preamble = $"{component.User.Mention} - {account.Backup?.UserName ?? account.Name}'s Earnings Boost rank history, with a first entry from {DiscordHelpers.TimeStamper(snapshots.First().Date)}.";

            var pager = new EBHistoryPager(lines, page, preamble, headerRow, dbUserId, accountIndex);
            await pager.UpdateComponentAsync(component);
        }

        [GeneratedRegex(@"^EI\d{16}$")]
        private static partial Regex MyRegex();
    }
}
