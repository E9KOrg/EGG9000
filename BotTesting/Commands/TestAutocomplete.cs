using Discord;
using Discord.Webhook;
using Discord.WebSocket;

using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Services;

using Google.Protobuf;

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace EGG9000.Bot.Commands {
    public class TestAutocompleteCommands {
        [SlashCommand(Description = "Test Button Interaction")]
        public static async Task MoveToCoop(FauxCommand command, [SlashParam(AutocompleteHandler = typeof(MoveToCoopCoopNameAutoComplete))]string coopid, [SlashParam(AutocompleteHandler = typeof(UserAccountAutoComplete))] string useraccount, ApplicationDbContext db) {
            var coop = await db.Coops.FirstOrDefaultAsync(x => x.Id == Guid.Parse(coopid));
            var userid = useraccount.Split("|")[0];
            var user = await db.DBUsers.FirstOrDefaultAsync(x => x.Id == Guid.Parse(userid));
            var account = user.EggIncAccounts.FirstOrDefault(x => x.Id == useraccount.Split("|")[1]);
            await command.RespondAsync($"Coop Name: {coop.Name}, EIID: {account.Id}", ephemeral: false);
        }
    }

    public class MoveToCoopCoopNameAutoComplete : AutoCompleteHandler {
        private readonly ApplicationDbContext _db;
        public MoveToCoopCoopNameAutoComplete(ApplicationDbContext db) {
            _db = db;
        }
        public async Task Run(SocketAutocompleteInteraction arg) {
            var coops = await _db.Coops.Include(x => x.Contract)
                .Where(x => EF.Functions.Like(x.Name, $"{(string)arg.Data.Current.Value}%") && !x.DeletedChannel)
                .Take(25).Select(x => new {x.Name, x.Id, Contract = x.Contract.Name}).ToListAsync();
            await arg.RespondAsync(null, coops.Select(c => new AutocompleteResult($"{c.Name} - {c.Contract}", c.Id.ToString())).ToArray());
        }
    }

    public class UserAccountAutoComplete : AutoCompleteHandler {
        private readonly ApplicationDbContext _db;
        public UserAccountAutoComplete(ApplicationDbContext db) {
            _db = db;
        }
        public async Task Run(SocketAutocompleteInteraction arg) {
            var users = await _db.DBUsers
                .Where(x => x.GuildId == arg.GuildId && EF.Functions.Like(x.DiscordUsername, $"%{(string)arg.Data.Current.Value}%"))
                .Take(10).ToListAsync();

            var accounts = users.SelectMany(x => x.EggIncAccounts.Select(y => new { User = x, Account = y }));

            var results = new List<AutocompleteResult>();
            foreach(var account in accounts) {
                if(account.User.EggIncAccounts.Count > 1) {
                    var name = account.User.Backups.FirstOrDefault(x => x.EggIncId == account.Account.Id)?.UserName;
                    results.Add(new AutocompleteResult($"{account.User.DiscordUsername} - {name ?? account.Account.Name}", $"{account.User.Id}|{account.Account.Id}"));
                } else {
                    results.Add(new AutocompleteResult($"{account.User.DiscordUsername}", $"{account.User.Id}|{account.Account.Id}"));
                }
            }

            await arg.RespondAsync(null, results.ToArray());
        }
    }
}
