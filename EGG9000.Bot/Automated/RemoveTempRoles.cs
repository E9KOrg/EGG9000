using Discord.WebSocket;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Bot.EggIncAPI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EGG9000.Bot.Helpers;
using Discord;
using EGG9000.Bot.Commands;
using Discord.Rest;
using Humanizer;
using static EGG9000.Bot.Helpers.FixedWidthTable;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Helpers;
using Ei;
using EGG9000.Common.Migrations;
using Polly;
using Microsoft.Data.SqlClient;
using EGG9000.Bot.Services;

namespace EGG9000.Bot.Automated {
    public class RemoveTempRoles : _UpdaterBase<RemoveTempRoles> {
        public static TimeSpan _updateInterval = TimeSpan.FromMinutes(1);
        private ApplicationDbContext _db;

        public RemoveTempRoles(
            Words words,
            IServiceProvider provider, 
            ApplicationDbContext context
        ) : base(_updateInterval, TimeSpan.Zero, provider) {
            _db = context;
        }



        public override async Task Run(object state, CancellationToken cancellationToken) {
            var rolesToRemove = await _db.TemporaryRoles.Where(x => x.Expires < DateTimeOffset.Now && !x.IsRemoved).ToListAsync();
            foreach(var role in rolesToRemove) {
                try {
                    var user = _client.Guilds.First(g => g.Id == role.GuildId).GetUser(role.UserId);
                    await user.RemoveRoleAsync(role.RoleId);
                } catch(Exception ex) {
                    Console.WriteLine($"Error: Unable to remove role from user with id {role.UserId}, exception was {ex.Message}");
                }
                role.IsRemoved = true;
            }
            await _db.SaveChangesAsync();
        }


    }
}
