using Discord;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static EGG9000.Bot.Helpers.FixedWidthTable;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public class VirtueCommand {
        //[SlashCommand(Description = "Virtue Status", AllowInDMs = true)]
        //public static async Task Virtue(FauxCommand command, ApplicationDbContext db, DiscordSocketClient discord) {
        //    await command.DeferAsync();

        //    var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
        //    if(dbUser == null) {
        //        await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
        //        return;
        //    }

        //    if(dbUser.EggIncAccounts.Count == 1) {
        //        var embed = await VirtueStringBuilder(discord, dbUser.GuildId, dbUser.EggIncAccounts.First(), db);
        //        await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; });
        //    } else {
        //        var builder = new ComponentBuilder();
        //        foreach(var account in dbUser.EggIncAccounts) {
        //            builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"VirtueAccountButton:{account.Id}|{((int)parameter)}|{command.User.Id}");
        //        }
        //        await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to chase with."; x.Components = builder.Build(); x.Embed = null; });
        //    }

        //    dbUser.UpdateAccounts();
        //    await db.SaveChangesAsync();
        //}
        
        //[ComponentCommand]
        //public static async Task VirtueAccountButton(SocketMessageComponent component, DiscordSocketClient _client, [ComponentData] string data, ApplicationDbContext db) {

        //    var dataObjs = data.Split("|");
        //    var originalUserId = ulong.Parse(dataObjs[2]);

        //    if(component.User.Id != originalUserId) {
        //        await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
        //        return;
        //    }

        //    await component.DeferAsync();

        //    var dbUser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
        //    if(dbUser is null) return;
        //    var account = dbUser.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);

        //    var embed = await VirtueStringBuilder(_client, dbUser.GuildId, dbUser.EggIncAccounts.First(), db);
        //    await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = null; });
        //}

        //private static async Task<Embed> VirtueStringBuilder(DiscordSocketClient discord, ulong guildId, EggIncAccount eggIncAccount, ApplicationDbContext db) {
        //    var guild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildId);



        //    var builder = new EmbedBuilder {
        //        Title = $"Virtue Progress - {eggIncAccount.Backup?.UserName ?? "(?)"}",
        //        Description = "",
        //    };


        //    return builder.WithAuthor(new EmbedAuthorBuilder().WithName("Virtue").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        //}
    }
}