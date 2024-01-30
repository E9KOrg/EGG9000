using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using EGG9000.Common.Commands;
using EGG9000.Common.Services;
using System.IO;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Discord.WebSocket;
using EGG9000.Common.Helpers;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static EGG9000.Bot.Commands.ContractCommandsSlash;
using Discord;

namespace EGG9000.Bot.Commands {
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

            await _viewInventory(command, dbuser, account, showinchannel);
        }

        [SlashCommand(Description = "View your inventory")]
        public static async Task ViewInventory(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount) {
            await command.DeferAsync();
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(dbuser is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID {userid}"); }); return; }
            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            await _viewInventory(command, dbuser, account);
        }

        public static async Task _viewInventory(FauxCommand command, DBUser user, EggIncAccount account, bool showInChannel = true) {
            var (B64, Config) = InventoryB64(account);
            if(string.IsNullOrEmpty(B64)) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("User inventory could not be converted."); }); return; }

            var image = new FileAttachment(new MemoryStream(Convert.FromBase64String(B64)), "Inventory.jpeg", "Inventory Image");
            await command.RespondWithFileAsync(image, text: " ", embed: _inventoryEmbed(user, account), ephemeral: !showInChannel);
            var response = command.GetOriginalResponseAsync().Result; // Get the response to edit it
            var baseUrl = response.Embeds.First().Image.ToString();
            var imageUrl = baseUrl.IndexOf("jpeg", StringComparison.OrdinalIgnoreCase) is int index && index != -1 ? baseUrl[..(index + "jpeg".Length)] : baseUrl;
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

        private class AfxSetBuilder {
            public ComponentBuilder ComponentBuilder { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }
            public AfxSetBuilder() { }
        }

        public static Color RandomColor() {
            var random = new Random();

            // Generate random values for red, green, and blue components.
            var red = (byte)random.Next(256);
            var green = (byte)random.Next(256);
            var blue = (byte)random.Next(256);

            // Create and return the Discord.Color.
            return new Color(red, green, blue);
        }

        [SlashCommand(Description = "Show off your saved Artifact Sets")]
        public static async Task SavedAfSets(FauxCommand command, ApplicationDbContext db, [SlashParam(AutocompleteHandler = typeof(PersonalUserAccountAutoComplete))] string useraccount, [SlashParam(Description = "Set # to statically display", Required = false, PositiveOnly = true)] int index = 0) {
            await command.DeferAsync();
            var lockSet = true;
            if(index == 0) {
                index = 1;
                lockSet = false;
            }
            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }
            EggIncAccount account = null;
            int accountIndex = 0;
            try {
                accountIndex = int.Parse(useraccount.Split("|")[1]);
                account = dbUser.EggIncAccounts[accountIndex];
            } catch(Exception) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); });
                return;
            }

            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null || afxSets.Count == 0) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Backup is empty, or no Artifact Sets were found for this account"); });
                return;
            }

            if(index < 1 || (index != 1 && index > afxSets.Count)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Set number `{index}` larger than maximum set number `{afxSets.Count}`."); });
                return;
            }

            var builder = AFXSetEmbedBuilder(dbUser, accountIndex, afxSets, afxSets[index - 1]);
            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Components = lockSet ? null : builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        [ComponentCommand]
        public static async Task LoadAFXSet(SocketMessageComponent component, [ComponentData] string data, ApplicationDbContext db) {

            var dataItems = data.Split(",");
            var discordId = ulong.Parse(dataItems[0] ?? "-1");
            var accountIndex = int.Parse(dataItems[1] ?? "-1");
            var currentSetIndex = int.Parse(dataItems[2] ?? "-1");

            if(discordId < 0 || accountIndex < 0 || currentSetIndex < 0) return;

            var user = db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
            if(user is null || user.EggIncAccounts.Count - 1 < accountIndex) return;

            var account = user.EggIncAccounts[accountIndex];
            var afxSets = account.Backup?.ArtifactSets;
            if(afxSets is null) return;

            var builder = AFXSetEmbedBuilder(user, accountIndex, afxSets, afxSets[currentSetIndex]);
            await component.UpdateAsync(x => {
                x.Content = "";
                x.Components = builder.ComponentBuilder?.Build();
                x.Embed = builder.EmbedBuilder.Build();
            });
        }

        private static AfxSetBuilder AFXSetEmbedBuilder(DBUser user, int accountIndex, List<List<EggIncArtifactInstance>> afxSets, List<EggIncArtifactInstance> currentSet) {
            var builder = new AfxSetBuilder() {
                ComponentBuilder = null
            };

            var componentBuilder = new ComponentBuilder();
            var buttonCount = 0;

            var currentSetIndex = afxSets.IndexOf(currentSet);

            var account = user.EggIncAccounts[accountIndex];
            var accText = user.EggIncAccounts.Count > 1 ? $"For account: {account.Backup?.UserName ?? "[No Name]"} ({account.Backup?.EarningsBonus.ToEggString() ?? "No EB"})" : "";

            var embedBuilder = new EmbedBuilder().WithAuthor(
                new EmbedAuthorBuilder()
                    .WithName($"Set {currentSetIndex + 1}")
                    .WithIconUrl("https://cdn.discordapp.com/emojis/877681508607987772.webp")
                ).WithColor(RandomColor())
                .WithDescription(GetAfxSetString(currentSet));
            if(accText != "") embedBuilder.WithFooter(new EmbedFooterBuilder().WithText(accText));

            if(currentSetIndex > 0 && afxSets.Count > 1 && afxSets[currentSetIndex - 1] is not null) {
                componentBuilder.WithButton($"← Set {currentSetIndex}", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex - 1}"); buttonCount++;
            }
            if(currentSetIndex < afxSets.Count - 1 && afxSets[currentSetIndex + 1] is not null) {
                componentBuilder.WithButton($"Set {currentSetIndex + 2} →", $"LoadAFXSet:{user.DiscordId},{accountIndex},{currentSetIndex + 1}"); buttonCount++;
            }
            if(buttonCount > 0) builder.ComponentBuilder = componentBuilder;

            builder.EmbedBuilder = embedBuilder;
            return builder;
        }

    }
}
