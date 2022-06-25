
using Discord;
using Discord.Net;
using Discord.WebSocket;

using EGG9000.Bot.Commands;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {

    public class DiscordUserService : IHostedService {
        private readonly DiscordHostedService _discord;
        private IConfiguration _configuration;
        private APILink _apiLink;
        private Words _words;
        private Bugsnag.IClient _bugsnag;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(50);

        public DiscordUserService(IConfiguration Configuration, DiscordHostedService discord, APILink apilink, Words words, Bugsnag.IClient bugsnag) {
            _discord = discord;
            _configuration = Configuration;
            _apiLink = apilink;
            _words = words;


            _bugsnag = bugsnag;
        }



        public Task StartAsync(CancellationToken cancellationToken) {
            _discord.UserJoined += Client_UserJoined;
            _discord.UserLeft += Client_UserLeft;
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _discord.UserJoined -= Client_UserJoined;
            _discord.UserLeft -= Client_UserLeft;
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }

        private Task Client_UserJoined(SocketGuildUser user) {
            _ = Client_UserJoined_Task(user);
            return Task.CompletedTask;
        }

        private async Task Client_UserJoined_Task(SocketGuildUser user) {
            var db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            await UserJoined(user, db);
        }

        private Task Client_UserLeft(SocketGuild guild, SocketUser user) {
            _ = Client_UserLeft_Task(guild, user);
            return Task.CompletedTask;
        }

        private async Task Client_UserLeft_Task(SocketGuild guild, SocketUser user) {
            var db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            await UserLeft(guild, user, db);
        }

        public async Task UserJoined(SocketGuildUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;

            var dbguild = await db.Guilds.AsQueryable().FirstOrDefaultAsync(x => x.DiscordSeverId == user.Guild.Id);
            if(dbguild == null) {
                return;
            }



            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

            if(dbuser != null && dbuser.GuildId == user.Guild.Id) {
                var generalChannel = await _discord.GetChannelAsync(GuildChannelType.General, user.Guild);
                await generalChannel.SendMessageAsync($"Welcome back {user.Mention}!");
                await RegisterCommandsSlash.CleanWelcomeChannel(user.Guild, _discord, user);
            } else {
                var welcomeChannel = await _discord.GetChannelAsync(GuildChannelType.Welcome, user.Guild);
                var rulesChannel = await _discord.GetChannelAsync(GuildChannelType.Rules, user.Guild);
                var msg = $"Welcome to the server {user.Mention}! Please read {rulesChannel.Mention} and then send the message __**/accept**__ when you are ready.";
                var talkChannel = user.Guild.TextChannels.FirstOrDefault(x => x.Id == 746509501271769210);
                if(talkChannel != null)
                    msg += $" If you have any questions feel free to ask us in {talkChannel.Mention}, we are glad you are here!";
                
                await welcomeChannel.SendMessageAsync(msg);
            }
        }

        public async Task UserLeft(SocketGuild guild, SocketUser user, ApplicationDbContext db) {
            if(user.IsBot)
                return;
            var dbuser = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == user.Id);

            if(guild.Id != dbuser.GuildId)
                return;

            if(dbuser != null) {
                dbuser.AcceptedRules = false;
                dbuser.GuildId = 0;
                await db.SaveChangesAsync();
            }
            await RegisterCommandsSlash.CleanWelcomeChannel(guild, _discord, user);
        }

    }
}
