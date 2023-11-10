using Discord;
using Discord.WebSocket;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.JsonData.EiAfxData;
using EGG9000.Common.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class ChasingCommand {
        [SlashCommand(Description = "Show you players ahead and behind you.", AllowInDMs = true)]
        public static async Task Chasing(FauxCommand command, [SlashParam] ChasingParameters parameter, ApplicationDbContext db, DiscordSocketClient discord, ILogger logger) {
            await command.RespondAsync("Getting backups...");

            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.DiscordId == command.User.Id);
            if(user == null) {
                await command.RespondAsync("⚠️ERROR: Unable to find backups for this user");
                return;
            }

            if(user.EggIncAccounts.Count == 1) {
                var contentString = await ChasingStringBuilder(discord, parameter, user.GuildId, user.EggIncAccounts.First(), db);
                await command.ModifyOriginalResponseAsync(contentString);
            } else {
                var builder = new ComponentBuilder();
                foreach(var account in user.EggIncAccounts) {
                    builder.WithButton($"{account.Backup?.UserName ?? "(No Name)"} {account.Backup?.EarningsBonus.ToEggString()}", customId: $"ChasingAccountButton:{account.Id}|{((int)parameter)}");
                }
                await command.ModifyOriginalResponseAsync(x => { x.Content = "Please select the account you would like to chase with."; x.Components = builder.Build(); });
            }

            user.UpdateAccounts();
            await db.SaveChangesAsync();
        }
        
        [ComponentCommand]
        public static async Task ChasingAccountButton(SocketMessageComponent component, DiscordSocketClient _client, Words _words, IServiceProvider _provider, [ComponentData] string data, ApplicationDbContext db) {
            var user = await db.DBUsers.FirstAsync(x => x.DiscordId == component.User.Id);
            if(user is null) return;
            var dataObjs = data.Split("|");
            var account = user.EggIncAccounts.FirstOrDefault(x => x.Id == dataObjs[0]);
            var parameter = (ChasingParameters)int.Parse(dataObjs[1]);

            var contentString = await ChasingStringBuilder(_client, parameter, user.GuildId, account, db);
            await component.UpdateAsync(x => { x.Components = null; x.Content = "Success"; });
            await component.Channel.SendMessageAsync(contentString);
        }

        private async static Task<string> ChasingStringBuilder(DiscordSocketClient discord, ChasingParameters parameter, ulong guildId, EggIncAccount eggIncAccount, ApplicationDbContext db) {
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
            })).Where(x => x.DiscordUser != null && x.Backup != null && x.Backup.Farms.Count > 0 && x.Account.Active).ToList();
            var unit = "";
            switch(parameter) {
                case ChasingParameters.EB:
                    accounts = accounts.OrderByDescending(x => x.Backup.EarningsBonus).ToList();
                    unit = "EB";
                    break;
                case ChasingParameters.SE:
                    accounts = accounts.OrderByDescending(x => x.Backup.SoulEggs).ToList();
                    unit = "SE";
                    break;
            }
            
            var userIndex = accounts.FindIndex(x => x.Backup.EggIncId == eggIncAccount.Backup.EggIncId);

            var stringBuilder = new StringBuilder();
            
            var counter = 0;
            var start = userIndex != accounts.Count - 1 ? userIndex - 3 : userIndex - 4;

            for(var i = start; counter < 5; i++) {
                if(i >= 0 && i < accounts.Count) {
                    switch(parameter) {
                        case ChasingParameters.EB:
                            stringBuilder.AppendFormat($"`{accounts[i].Backup.UserName, -20}{accounts[i].Backup.EarningsBonus.ToEggString(true, 2), 8} {unit}`");
                            stringBuilder.AppendLine();
                            break;
                        case ChasingParameters.SE:
                            stringBuilder.AppendFormat($"`{accounts[i].Backup.UserName, -20}{accounts[i].Backup.SoulEggs.ToEggString(true, 2), 8} {unit}`");
                            stringBuilder.AppendLine();
                            break;
                    }

                    counter++;
                }

                if(i == accounts.Count - 1) {
                    break;
                }
            }
            return stringBuilder.ToString();
        }
        public enum ChasingParameters {
            EB = 0,
            SE = 1
        }
    }
}