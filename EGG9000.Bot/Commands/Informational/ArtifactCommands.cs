using Discord;
using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;
using EGG9000.Common.Helpers.AfxSets;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.CommonTypes.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands.Informational {
    public static class ArtifactCommands {

        [SlashCommand(Description = "View a user's inventory", AdminOnly = StaffOnlyLevel.FarmHand, ParentCommand = "a")]
        public static async Task ViewInventory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, [SlashParam(Required = false)] bool showinchannel = false) {
            await command.DeferAsync(ephemeral: !showinchannel);
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(dbuser is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID {userid}"); }); return; }
            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            await _viewInventory(command, db, dbuser, account, showinchannel);
        }

        [SlashCommand(Description = "View your inventory", AllowInDMs = true)]
        public static async Task ViewInventory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.DeferAsync();
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
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Stop trying to view other's inventories."); });
                return;
            }
            
            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            await _viewInventory(command, db, dbuser, account);
        }

        public static async Task _viewInventory(FauxCommand command, ApplicationDbContext db, DBUser user, EggIncAccount account, bool showInChannel = true) {
            //Pull and save a fresh backup
            var backup = new CustomBackup((await EggIncApi.FirstContact(account.Id)).Backup, await db.CachedEiContractsAsync(), account.Backup ?? null);
            if(account.Backup is null) account.Backup = backup;
            else account.Backup.ArtifactHall = backup.ArtifactHall;
            user.UpdateAccounts();
            await db.SaveChangesAsync();

            if(account.Backup is null || account.Backup.ArtifactHall is null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Backup came back as empty from the Egg, Inc. API."); });
                return;
            }

            var (B64, Config) = await InventoryB64(account);
            if(string.IsNullOrEmpty(B64) || B64.StartsWith("$ERROR$:")) { 
                await command.ModifyOriginalResponseAsync(x => { 
                    x.Content = ""; 
                    x.Embed = EmbedError($"User inventory could not be converted.${(B64.StartsWith("$ERROR$:") ? $"\n```{B64.Replace("$ERROR$:", "")}```" : "")}"); 
                }); 
                return; 
            }

            var image = new FileAttachment(new MemoryStream(Convert.FromBase64String(B64)), "Inventory.jpeg", "Inventory Image");
            await command.RespondWithFileAsync(image, text: " ", embed: _inventoryEmbed(user, account), ephemeral: !showInChannel);
            var response = command.GetOriginalResponseAsync().Result; // Get the response to edit it
            var imageUrl = TrimImageUrl(response.Embeds.First().Image.ToString());
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = _inventoryEmbed(user, account, imageUrl);
            });
        }

        public static Embed _inventoryEmbed(DBUser dbuser, EggIncAccount account, string imageUrl = "") {
            var description = $"Inventory of <@{dbuser.DiscordId}> - `{account.Backup?.UserName ?? "(No Name)"} ({account.Backup.EarningsBonus.ToEggString()})`";
            var builder = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithDescription(description)
                .WithAuthor(new EmbedAuthorBuilder()
                    .WithName("EGG9000")
                    .WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")
                )
                .WithImageUrl("attachment://Inventory.jpeg");

            if(imageUrl != "") { builder.WithTitle("Link to full resolution image").WithUrl(imageUrl); }
            return builder.Build();
        }

        [SlashCommand(Description = "Show off your saved Artifact Sets")]
        public static async Task SavedAfSets(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.DeferAsync();

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            EggIncAccount account = null;
            var accountIndex = 0;
            try {
                accountIndex = int.Parse(useraccount.Split("|")[1]);
                account = dbUser.EggIncAccounts[accountIndex];
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); });
                return;
            }

            // Fresh pull, update only the sets. Use the fresh backup as the initial one if the
            // account had none yet (new/failed registration) rather than bailing on usable data.
            var fresh = new CustomBackup((await EggIncApi.FirstContact(account.Id)).Backup, await db.CachedEiContractsAsync(), account.Backup ?? null);
            if(account.Backup is null) account.Backup = fresh;
            else account.Backup.ArtifactSets = fresh.ArtifactSets;
            dbUser.UpdateAccounts();
            await db.SaveChangesAsync();

            var sets = account.Backup.ArtifactSets;
            if(sets is null || sets.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("No Artifact Sets were found for this account."); });
                return;
            }

            var hash = AfxSetsHash.Compute(sets);
            List<string> urls;
            if(account.AfxSetsImageHash == hash && account.AfxSetsImageUrls is { Count: > 0 }) {
                urls = account.AfxSetsImageUrls;
            } else {
                var (pages, renderError) = await AfxSetsRender.AfxSetsB64(account);
                if(pages is null || pages.Count == 0) {
                    await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Artifact set images could not be generated.\n```{renderError}```"); });
                    return;
                }
                var files = pages.Select((b, i) => new FileAttachment(new MemoryStream(Convert.FromBase64String(b)), $"afxset_page_{(i + 1):D2}.jpeg")).ToList();
                var resp = await command.RespondWithFilesAsyncGettingMessage(files, text: "Uploading artifact sets...");
                urls = [.. resp.Attachments.OrderBy(a => a.Filename).Select(a => TrimImageUrl(a.Url))];
                account.AfxSetsImageHash = hash;
                account.AfxSetsImageUrls = urls;
                dbUser.UpdateAccounts();
                await db.SaveChangesAsync();
            }

            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                // Drop the uploaded page attachments now that we have their CDN URLs; the embed
                // references those URLs directly so the loose image files don't clutter the message.
                x.Attachments = new List<FileAttachment>();
                x.Embeds = new[] { AfxSetsImageEmbed(dbUser, account, urls, 0), AfxSetsDetailEmbed(null) };
                x.Components = AfxSetsComponents(dbUser, accountIndex, sets, urls.Count, 0);
            });
        }

        public static string TrimImageUrl(string baseUrl) {
            var i = baseUrl.IndexOf("&format", StringComparison.OrdinalIgnoreCase);
            return i != -1 ? baseUrl[..i] : baseUrl;
        }

        private static Embed AfxSetsImageEmbed(DBUser user, EggIncAccount account, List<string> urls, int page) {
            var name = account.Backup?.UserName ?? "(No Name)";
            var eb = account.Backup?.EarningsBonus.ToEggString() ?? "No EB";
            return new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithAuthor(new EmbedAuthorBuilder().WithName("EGG9000").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp"))
                .WithDescription($"Artifact Sets of <@{user.DiscordId}> - `{name} ({eb})`")
                .WithTitle("Link to full resolution image")
                .WithUrl(urls[page])
                .WithImageUrl(urls[page])
                .WithFooter(new EmbedFooterBuilder().WithText($"Page {page + 1}/{urls.Count}"))
                .Build();
        }

        private static Embed AfxSetsDetailEmbed(List<EggIncArtifactInstance> set, int globalIndex = -1) {
            if(set is null) {
                return new EmbedBuilder().WithColor(Color.DarkGrey).WithDescription("Select a set from the dropdown to view its artifacts and explorer links.").Build();
            }
            var emoji = GetAfxSetString(set);
            var links = set.Select(a => {
                var artifactLink = $"[{a.Artifact}]({AfxExplorerLink.Url(a, false)})";
                // Collapse duplicate stones into "Name x N".
                var stoneLinks = string.Concat((a.Stones ?? [])
                    .GroupBy(s => (s.Id, s.Tier))
                    .Select(g => {
                        var s = g.First();
                        var label = g.Count() > 1 ? $"{s.Artifact} x {g.Count()}" : s.Artifact;
                        return $" + [{label}]({AfxExplorerLink.Url(s, true)})";
                    }));
                return artifactLink + stoneLinks;
            });
            return new EmbedBuilder()
                .WithColor(Color.Gold)
                .WithAuthor(new EmbedAuthorBuilder().WithName($"Set {globalIndex + 1}").WithIconUrl("https://cdn.discordapp.com/emojis/877681508607987772.webp"))
                .WithDescription($"{emoji}\n\n**Explorer Links:**\n{string.Join("\n", links)}")
                .Build();
        }

        private static MessageComponent AfxSetsComponents(DBUser user, int accountIndex, List<List<EggIncArtifactInstance>> sets, int pageCount, int page) {
            var cb = new ComponentBuilder();
            var perPage = AfxSetsCreatorConfig.DefaultSetsPerPage;
            var pageStart = page * perPage;

            var menu = new SelectMenuBuilder().WithCustomId($"AfxSetsSelect:{user.DiscordId},{accountIndex},{page}").WithPlaceholder("Select a set to view details");
            var added = 0;
            for(var i = pageStart; i < pageStart + perPage && i < sets.Count; i++) {
                if(sets[i].Count == 0) continue; // empty sets are not selectable
                menu.AddOption($"Set {i + 1}", i.ToString());
                added++;
            }
            if(added > 0) cb.WithSelectMenu(menu);

            cb.WithButton("◀", $"AfxSetsPage:{user.DiscordId},{accountIndex},{page - 1}", ButtonStyle.Secondary, disabled: page <= 0);
            cb.WithButton("▶", $"AfxSetsPage:{user.DiscordId},{accountIndex},{page + 1}", ButtonStyle.Secondary, disabled: page >= pageCount - 1);
            return cb.Build();
        }

        [ComponentCommand]
        public static async Task AfxSetsPage(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var parts = data.Split(",");
            if(parts.Length < 3) return;
            var discordId = ulong.Parse(parts[0]);
            var accountIndex = int.Parse(parts[1]);
            var page = int.Parse(parts[2]);

            var user = db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
            if(user is null || user.EggIncAccounts.Count - 1 < accountIndex) return;
            var account = user.EggIncAccounts[accountIndex];
            var sets = account.Backup?.ArtifactSets;
            var urls = account.AfxSetsImageUrls;
            if(sets is null || urls is null || page < 0 || page >= urls.Count) return;

            await component.UpdateAsync(x => {
                x.Content = "";
                x.Embeds = new[] { AfxSetsImageEmbed(user, account, urls, page), AfxSetsDetailEmbed(null) };
                x.Components = AfxSetsComponents(user, accountIndex, sets, urls.Count, page);
            });
        }

        [ComponentCommand]
        public static async Task AfxSetsSelect(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {
            var parts = data.Split(",");
            if(parts.Length < 3) return;
            var discordId = ulong.Parse(parts[0]);
            var accountIndex = int.Parse(parts[1]);
            var page = int.Parse(parts[2]);
            var selected = int.Parse(component.Data.Values.First());

            var user = db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
            if(user is null || user.EggIncAccounts.Count - 1 < accountIndex) return;
            var account = user.EggIncAccounts[accountIndex];
            var sets = account.Backup?.ArtifactSets;
            var urls = account.AfxSetsImageUrls;
            if(sets is null || urls is null || selected < 0 || selected >= sets.Count || page < 0 || page >= urls.Count) return;

            await component.UpdateAsync(x => {
                x.Content = "";
                x.Embeds = new[] { AfxSetsImageEmbed(user, account, urls, page), AfxSetsDetailEmbed(sets[selected], selected) };
                x.Components = AfxSetsComponents(user, accountIndex, sets, urls.Count, page);
            });
        }

    }
}
