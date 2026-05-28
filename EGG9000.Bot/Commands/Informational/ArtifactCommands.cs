using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EGG9000.Common.EggIncAPI;
using EGG9000.Bot.Interactions;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static EGG9000.Bot.Commands.DiscordEnums.AutoCompleteHandlers;
using static EGG9000.Common.Helpers.ArtifactHelpers;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class ArtifactCommands {

        public static async Task _viewInventory(SocketInteraction command, ApplicationDbContext db, DBUser user, EggIncAccount account, bool showInChannel = true) {
            var backup = new CustomBackup((await EggIncApi.FirstContact(account.Id)).Backup, account.Backup ?? null);
            account.Backup.ArtifactHall = backup.ArtifactHall;
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
            await command.RespondWithFilesAsyncGettingMessage([image], text: " ", embed: _inventoryEmbed(user, account), ephemeral: !showInChannel);
            var response = command.GetOriginalResponseAsync().Result; // Get the response to edit it
            var baseUrl = response.Embeds.First().Image.ToString();
            var formatIndex = baseUrl.IndexOf("&format", StringComparison.OrdinalIgnoreCase);
            var imageUrl = formatIndex is int index && index != -1 ? baseUrl[..(index + "&format".Length)] : baseUrl;
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

        public static Color RandomColor() {
            var random = new Random();
            var red = (byte)random.Next(256);
            var green = (byte)random.Next(256);
            var blue = (byte)random.Next(256);
            return new Color(red, green, blue);
        }
    }

    public class ArtifactModule(IDbContextFactory<ApplicationDbContext> dbFactory) : EGG9000.Bot.Interactions.E9KModuleBase(dbFactory) {

        private class AfxSetBuilder {
            public ComponentBuilder ComponentBuilder { get; set; }
            public EmbedBuilder EmbedBuilder { get; set; }
            public AfxSetBuilder() { }
        }

        [SlashCommand("viewinventory", "View your inventory")]
        [EnabledInDm(true)]
        public async Task ViewInventory([Autocomplete(typeof(PersonalUserAccountAutoComplete))][Summary("useraccount")] string useraccount) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) {
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

            if(dbuser.DiscordId != command.User.Id) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Stop trying to view other's inventories."); });
                return;
            }

            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            await ArtifactCommands._viewInventory(command, Db, dbuser, account);
        }

        [SlashCommand("savedafsets", "Show off your saved Artifact Sets")]
        public async Task SavedAfSets([Autocomplete(typeof(PersonalUserAccountAutoComplete))][Summary("useraccount")] string useraccount, [Summary("index", "Set # to statically display")][MinValue(0)] int index = 0) {
            var command = Context.Interaction;
            await command.DeferAsync();
            var lockSet = true;
            if(index == 0) {
                index = 1;
                lockSet = false;
            }
            var dbUser = await Db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
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

        [ComponentInteraction("LoadAFXSet:*", ignoreGroupNames: true)]
        public async Task LoadAFXSet(string data) {
            var component = (SocketMessageComponent)Context.Interaction;

            var dataItems = data.Split(",");
            var discordId = ulong.Parse(dataItems[0] ?? "-1");
            var accountIndex = int.Parse(dataItems[1] ?? "-1");
            var currentSetIndex = int.Parse(dataItems[2] ?? "-1");

            if(discordId < 0 || accountIndex < 0 || currentSetIndex < 0) return;

            var user = Db.DBUsers.FirstOrDefault(x => x.DiscordId == discordId);
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
                ).WithColor(ArtifactCommands.RandomColor())
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

    public partial class AdminModule {
        [Discord.Interactions.SlashCommand("viewinventory", "View a user's inventory")]
        public async Task ViewInventory([Discord.Interactions.Autocomplete(typeof(UserAccountAutoComplete))][Discord.Interactions.Summary("useraccount")] string useraccount, [Discord.Interactions.Summary("showinchannel")] bool showinchannel = false) {
            await Context.Interaction.DeferAsync(ephemeral: !showinchannel);
            var userid = useraccount.Split("|")[0];
            DBUser dbuser = null;
            try { dbuser = await Db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid)); } catch(Exception) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(dbuser is null) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"DB user could not be found from user ID {userid}"); }); return; }
            EggIncAccount account = null;
            try { account = dbuser.EggIncAccounts[int.Parse(useraccount.Split("|")[1])]; } catch(Exception) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("Please select an account from the list, instead of typing an input."); }); return; }
            if(account is null) { await Context.Interaction.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"User account for {userid} could not be found"); }); return; }

            await ArtifactCommands._viewInventory(Context.Interaction, Db, dbuser, account, showinchannel);
        }
    }
}
