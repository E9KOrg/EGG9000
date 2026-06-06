using Discord;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.CommonTypes.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands.Informational {
    public static class EBHistoryCommand {

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

        [SlashCommand(Description = "View key points in your EB history")]
        public static async Task EBHistory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount, [SlashParam(Required = false)] bool showinchannel = false) {
            await command.DeferAsync(ephemeral: !showinchannel);
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) {
                //Don't keep EIDs in plaintext in the command history
                if(Regex.IsMatch(useraccount, @"^EI\d{16}$")) {
                    await command.DeleteOriginalResponseAsync();
                    await command.Channel.SendMessageAsync(embed: EmbedError($"{command.User.Mention} - Please select an account from the list, instead of typing an input.\n\n**(Command use deleted to hide your EID)**."));
                } else {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); });
                }
                return;
            }
            if(dbuser is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID {userid}"); }); return; }

            //I hate that people are like this
            if(dbuser.DiscordId != command.User.Id) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Stop trying to run commands on others' accounts."); });
                return;
            }

            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            var snapshots = await db.UserSnapShots.AsQueryable().Where(x => x.UserId == dbuser.Id && x.EggIncID == account.Id).ToListAsync();

            if(!snapshots.Any()) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"No EB history for {userid} ({account.Backup?.UserName ?? account.Name}) could not be found"); }); return; }

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

            var sb = new StringBuilder();
            sb.Append($"{command.User.Mention} - {account.Backup?.UserName ?? account.Name}'s Earnings Boost rank history, with a first entry from {DiscordHelpers.TimeStamper(snapshots.First().Date)}.");
            sb.AppendLine("```");
            sb.AppendLine($"Date        EB{new string(' ', longestEBLength - 2)}   Rank{new string(' ', longestRankLength - 2)}");
            foreach(var customEntry in entries) {
                sb.AppendLine(customEntry.ToString());
            }
            sb.AppendLine("```");

            var builder = new EmbedBuilder();
            builder.Title = "EB History";
            builder.Description = sb.ToString();
            builder.Color = Color.DarkGreen;

            await command.ModifyOriginalResponseAsync(c => { c.Content = ""; c.Embed = builder.Build(); });
        }

    }
}
