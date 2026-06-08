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
    public class ChasingCommand {
        [SlashCommand(Description = "Show you players ahead and behind you.", AllowInDMs = true)]
        public static async Task Chasing(FauxCommand command, [SlashParam] ChasingParameters parameter, ApplicationDbContext db, DiscordSocketClient discord) {
            await command.DeferAsync();

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(dbUser == null) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Unable to locate DBUser entry for <@{command.User.Id}>.\nAre you registered?"); });
                return;
            }

            if(dbUser.EggIncAccounts.Count == 1) {
                var embed = await ChasingStringBuilder(discord, parameter, dbUser.GuildId, dbUser.EggIncAccounts.First(), db);
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; });
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in dbUser.EggIncAccounts) {
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"ChasingAccountButton:{account.Id}|{((int)parameter)}|{command.User.Id}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to chase with."; x.Components = builder.Build(); x.Embed = null; });
            }

            dbUser.UpdateAccounts();
            await db.SaveChangesAsync();
        }
        
        [ComponentCommand]
        public static async Task ChasingAccountButton(SocketMessageComponent component, DiscordSocketClient _client, [ComponentData] string data, ApplicationDbContext db) {

            var dataObjs = data.Split("|");
            var originalUserId = ulong.Parse(dataObjs[2]);

            if(component.User.Id != originalUserId) {
                if(component.HasResponded)
                    await component.ModifyOriginalResponseAsync(x => { x.Content = null; x.Embed = EmbedError("This wasn't yours to run - don't click others' commands!"); x.Components = null; });
                else
                    await component.RespondAsync(embed: EmbedError("This wasn't yours to run - don't click others' commands!"), ephemeral: true);
                return;
            }

            if(!component.HasResponded) await component.DeferAsync();

            var dbUser = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            if(dbUser is null) return;
            var account = dbUser.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);
            var parameter = (ChasingParameters)int.Parse(dataObjs[1]);

            var embed = await ChasingStringBuilder(_client, parameter, dbUser.GuildId, dbUser.EggIncAccounts.First(), db);
            await component.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = embed; x.Components = null; });
        }

        private static async Task<Embed> ChasingStringBuilder(DiscordSocketClient discord, ChasingParameters parameter, ulong guildId, EggIncAccount eggIncAccount, ApplicationDbContext db) {
            var guild = await db.Guilds.AsQueryable().FirstAsync(x => x.Id == guildId);

            var rawUsers = await db.DBUsers.AsQueryable().Where(x => x.GuildId == guildId && !x.TempDisabled).Select(x => new {
                x.DiscordId,
                x.DiscordUsername,
                x.GuildId,
                x.Id,
                x._CustomBackups,
                x._eggIncIds,
                x.Registered,
                DBUser = x
            }).ToListAsync();
            
            var accounts = rawUsers.SelectMany(x => x.DBUser.EggIncAccounts.Select(y => new Prefarm.LeaderboardUser {
                User = x.DBUser,
                Backup = y.Backup,
                DiscordUser = discord.Guilds.First(g => g.Id == x.GuildId).Users.FirstOrDefault(du => du.Id == x.DiscordId),
                TotalContracts = x.DBUser.GuildCoops,
                TotalCS = y.Backup?.TotalCS ?? 0,
                SeasonCS = y.Backup?.SeasonCS ?? 0
            })).Where(x => x.DiscordUser != null && x.Backup != null && x.Backup.Farms.Count > 0).ToList();

#if DEV9002
#else
            accounts = accounts.Where(x => x.Account.Active).ToList();
#endif
            var unit = "";
            switch(parameter) {
                case ChasingParameters.EB:
                    accounts = [..accounts.OrderByDescending(x => x.Backup.EarningsBonus)];
                    unit = "EB";
                    break;
                case ChasingParameters.SE:
                    accounts = [..accounts.OrderByDescending(x => x.Backup.SoulEggs)];
                    unit = "SE";
                    break;
            }
            
            var userIndex = accounts.FindIndex(x => x.Backup.EggIncId == eggIncAccount.Backup.EggIncId);

            var stringBuilder = new StringBuilder();
            
            var counter = 0;
            var start = userIndex != accounts.Count - 1 ? userIndex - 3 : userIndex - 4;

            var table = new List<List<FixedWidthCell>>();

            for(var i = start; counter < 5; i++) {
                if(i >= 0 && i < accounts.Count) {
                    switch(parameter) {
                        case ChasingParameters.EB:
                            table.Add([new((i + 1).ToString()), new(ContractUpdater.Truncate(accounts[i].Backup.UserName, 17)), new($"{accounts[i].Backup.EarningsBonus.ToEggString(true, 2)} {unit}", CellAlignment.Right)]);
                            break;
                        case ChasingParameters.SE:
                            table.Add([new((i + 1).ToString()), new(ContractUpdater.Truncate(accounts[i].Backup.UserName, 17)), new($"{accounts[i].Backup.SoulEggs.ToEggString(true, 2)} {unit}", CellAlignment.Right)]);
                            break;
                    }

                    counter++;
                }

                if(i == accounts.Count - 1) {
                    break;
                }
            }

            var rows = GetTableListFormatted(table);

            var builder = new EmbedBuilder {
                Title = $"{parameter} Chasing - {eggIncAccount.Backup?.UserName ?? "(?)"}",
                Description = string.Join("\n", rows),
            };


            return builder.WithAuthor(new EmbedAuthorBuilder().WithName("Chasing").WithIconUrl("https://cdn.discordapp.com/avatars/514257192803893272/47be266c55cab32eacfb33c9affc82dd.webp")).Build();
        }
        public enum ChasingParameters {
            EB = 0,
            SE = 1
        }
    }
}