
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

    public class TextCommandService : IHostedService {
        private readonly DiscordSocketClient _discord;
        private IConfiguration _configuration;
        private APILink _apiLink;
        private Words _words;
        private Bugsnag.IClient _bugsnag;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(50);
        private static List<DBUser> users;

        public TextCommandService(IConfiguration Configuration, DiscordSocketClient discord, APILink apilink, Words words, Bugsnag.IClient bugsnag) {
            _discord = (DiscordSocketClient)discord;
            _configuration = Configuration;
            _apiLink = apilink;
            _words = words;


            _bugsnag = bugsnag;
        }



        public Task StartAsync(CancellationToken cancellationToken) {
            _discord.MessageReceived += MessageReceived;
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _discord.MessageReceived -= MessageReceived;
            Console.WriteLine($"Stopped listening to text commands");
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }

        private Task MessageReceived(SocketMessage message) {
            _ = Task.Run(() => MessageReceivedTask(message));
            return Task.CompletedTask;
        }

        private async Task MessageReceivedTask(SocketMessage message) {
            var db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            if(((IMessage)message).Type == MessageType.UserPremiumGuildSubscription) {
                if(message.Channel.Id == 680431628950044676) { //CP Welcome Channel
                    var cpGeneralChannel = _discord.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 656455568353132546);
                    await MeritCommands.CreateMerit("Boosted the server!", db, _discord, message.Author, Guid.Empty, cpGeneralChannel);
                    await cpGeneralChannel.SendMessageAsync($"{message.Author.Mention} just boosted the server!");
                }
                return;
            }
            try {
                //if(message.Content.StartsWith("*testemoji")) {
                //    var args = message.Content.Split(' ').Skip(1).ToArray();
                //    await MiscCommands.TestEmoji(message, args);
                //}
                //return;
                if(message.Content.StartsWith("!egg")) {
                    return;
                }

                if(message.Content.StartsWith("/") && (message.Interaction is null || message.Interaction.Type != InteractionType.ApplicationCommand)) {
                    await message.Channel.SendMessageAsync($"**ERROR:** Looks like you attempted to run a command but Discord didn't recognize it. As you type the command a menu should pop up and you select the command you want from that menu. If you continue to have trouble try force closing Discord.", messageReference: new MessageReference(message.Id));
                }


                if(message.Content.StartsWith("!")) {
                    Console.WriteLine($"Command: {message}");
                    var command = message.Content.Substring(1).Split(' ')[0].ToLower().Replace("-", "").Replace("  ", " ").Replace("  ", " ");
                    var args = message.Content.Split(' ').Skip(1).ToArray();

                    if(users == null) {
                        users = new List<DBUser>();
                    }
                    if(!users.Any(x => x.DiscordId == message.Author.Id)) {
                        var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
                        if(user != null) {
                            users.Add(user);
                        }
                    }

                    if(!users.Any(x => x.DiscordId == message.Author.Id && x.AcceptedRules) && command != "accept") {
                        var user = await db.DBUsers.AsQueryable().FirstOrDefaultAsync(x => x.DiscordId == message.Author.Id);
                        if(user == null || !user.AcceptedRules) {
                            var rulesChannel = ((SocketGuildUser)message.Author).Guild.GetRulesChannel();
                            await message.Channel.SendMessageAsync($"{message.Author.Mention} Please read {rulesChannel.Mention} and then type the command !accept to show you have accepted the rules before running any other commands");
                            return;
                        } else {
                            users.RemoveAll(x => x.Id == user.Id);
                            users.Add(user);
                        }
                    }


                    var isAdmin = false;

                    try {
                        isAdmin = ((SocketGuildUser)message.Author).Roles.Any(x => x.Permissions.ManageChannels ||
                            x.Name.ToLower().Contains("admin") || x.Name.ToLower().Contains("staff")) ||
                            message.Author.Username == "kendrome" ||
                            ((SocketGuildUser)message.Author).GuildPermissions.ManageChannels;
                    } catch(Exception) {

                    }

                    //Admin commands
                    if(isAdmin) {
                        switch(command) {
                            case "say":
                                await RegisterCommands.Say(message, args);
                                return;
                            case "clean":
                                await RegisterCommands.Clean(message, _discord);
                                return;
                            case "makepublic":
                                await ContractCommands.MakePublic(message, db);
                                return;
                            case "makeprivate":
                                await ContractCommands.MakePrivate(message, db);
                                return;
                            case "testadmin":
                                await message.Channel.SendMessageAsync($"You are an admin!");
                                return;
                            case "startuser":
                                await ContractCommands.StartUser(message, args, db, _discord, _apiLink, _words, fill: false);
                                return;
                            case "startfill":
                                await ContractCommands.StartUser(message, args, db, _discord, _apiLink, _words, fill: true);
                                return;
                            case "startempty":
                                await ContractCommands.StartEmpty(message, args, db, _discord, _apiLink, _words);
                                return;
                            case "startpercent":
                                await ContractCommands.StartPercent(message, args, db, _discord, _apiLink, _words);
                                return;
                            case "startall":
                                await ContractCommands.StartAll(message, args, db, _discord, _apiLink, _words);
                                return;
                            case "update": {
                                    var channel = (SocketTextChannel)message.Channel;
                                    if(channel.Category?.Name.ToLower().Contains("contracts") ?? false) {
                                        await ContractCommands.Update(message, args, db, _discord);
                                    } else if(channel.Name.ToLower().Contains("leaderboard")) {
                                        await ContractCommands.Update(message, args, db, _discord);
                                        //_leaderboardUpdater.Update();
                                    } else {
                                        await message.Channel.SendMessageAsync($"This command only works in a contract or leaderboard channel. {channel.Category.Name}");
                                    }
                                    return;
                                }
                            case "missingregistrations":
                                await MissingRegistrations.Run(message, db, _discord);
                                return;
                            case "removenull":
                                await RegisterCommands.RemoveNull(message, args, db, _discord, _apiLink);
                                return;
                            case "showduplicates":
                                await RegisterCommands.ShowDuplicates(message, args, db, _discord);
                                return;
                            case "removeduplicates":
                                await RegisterCommands.RemoveDuplicates(message, args, db, _discord);
                                return;
                            case "delete": {
                                    var channel = (SocketTextChannel)message.Channel;
                                    if(channel.Category.Name.ToLower().Contains("contracts")) {
                                        await ContractCommands.Delete(message, args, db, _discord);
                                        return;
                                    } else {
                                        await message.Channel.SendMessageAsync($"This command only works in a contract or co-op channel. {channel.Category.Name}");
                                    }
                                    return;
                                }
                            case "move":
                                await ContractCommands.Move(message, args, db, _discord);
                                return;
                            case "remove":
                                await ContractCommands.Remove(message, args, db, _discord);
                                return;
                            case "removename":
                                await RegisterCommands.RemoveEggName(message, args, db, _discord, _apiLink);
                                return;
                            case "removeid":
                                await RegisterCommands.RemoveID(message, args, db, _discord, _apiLink);
                                return;
                            case "leavecoop":
                                await RegisterCommands.LeaveCoop(message, args, db, _discord);
                                return;
                            case "addprefarmers":
                                await ContractCommands.AddPrefarmers(message, args, db, _discord, _apiLink);
                                return;
                            case "disable":
                                await RegisterCommands.Disable(message, args, db, _discord);
                                return;
                            case "enable":
                                await RegisterCommands.Enable(message, args, db, _discord);
                                return;
                            case "fixreference":
                                await ContractCommands.FixReference(message, args, db);
                                return;
                            case "checkroles": {
                                    var guild = _discord.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
                                    await guild.DownloadUsersAsync();
                                    var count = guild.Users.Count(u => message.MentionedRoles.All(r => u.Roles.Any(x => x.Id == r.Id)));
                                    await message.Channel.SendMessageAsync($"{count} users with the roles {String.Join(", ", message.MentionedRoles.Select(x => x.Mention))}");
                                    return;
                                }
                            case "listusers": {
                                    var guild = _discord.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
                                    await guild.DownloadUsersAsync();
                                    var users = guild.Users.Where(u => message.MentionedRoles.All(r => u.Roles.Any(x => x.Id == r.Id)));
                                    var userList = String.Join(", ", users.Select(x => x.Mention));
                                    while(userList.Length > 2000) {
                                        var index = userList.LastIndexOf(", ", 2000);
                                        if(message.MentionedChannels.Count > 0) {
                                            await ((SocketTextChannel)message.MentionedChannels.First()).SendMessageAsync(userList.Substring(0, index));
                                        } else {
                                            await message.Channel.SendMessageAsync(userList.Substring(0, index));
                                        }
                                        userList = userList.Substring(index);
                                    }

                                    if(message.MentionedChannels.Count > 0) {
                                        await ((SocketTextChannel)message.MentionedChannels.First()).SendMessageAsync(userList);
                                    } else {
                                        await message.Channel.SendMessageAsync(userList);
                                    }
                                    return;
                                }

                            case "rename": await MiscCommands.RenameCoop(message, args, db); return;
                                //case "staffcoops":
                                //    await MiscCommands.StaffCoops(message, args, db, client);
                                //    return;

                        }
                    }


                    //Farm Hands
                    //var isFarmHand = false;
                    //try {
                    //    isFarmHand = ((SocketGuildUser)message.Author).Roles.Any(x =>
                    //        x.Name.ToLower().Contains("farm hand"));
                    //} catch(Exception) {

                    //}


                    if(!isAdmin && message.MentionedUsers.Count > 0) {
                        return;
                    }
                    switch(command) {
                        //case "join":
                        //    await ContractCommands.Join(message, db, _apiLink, _discord);
                        //    return;
                        //case "pingonfull":
                        //    await MiscCommands.PingOnFull(message, args, db);
                        //    return;
                        //case "userstatus":
                        //    await RegisterCommands.userstatus(message, args, db, _discord, _apiLink);
                        //    return;
                        //case "showeb":
                        //    await RegisterCommands.ShowEB(message, args, db);
                        //    return;
                        //case "hideeb":
                        //    await RegisterCommands.HideEB(message, args, db);
                        //    return;
                        //case "takeabreak":
                        //    await RegisterCommands.TakeABreak(message, args, db, _discord);
                        //    return;
                        //case "skipnope":
                        //    await ContractCommands.SkipNoPe(message, args, db, _discord);
                        //    return;
                        //case "unskipnope":
                        //    await ContractCommands.UnSkipNoPe(message, args, db, _discord);
                        //    return;
                        //case "skip":
                        //    await ContractCommands.Skip(message, args, db, _discord);
                        //    return;
                        //case "starter": await _contractUpdater.Starter(message, args, db, client); break;
                        //case "removestarter": await _contractUpdater.RemoveStarter(message, args, db, client); break;

                        //case "opencoops": await Contracts.OpenCoops(message, db); break; //**Need to update for multiple guilds

                        //case "addcoop":
                        //    await ContractCommands.AddCoop(message, args, db, _discord, _apiLink);
                        //    break;
                        ////case "mystatus": await ContractCommands.MyStatus(message, db); break;
                        ////case "getstatus": await ContractCommands.GetStatus(message, args, db); break;
                        //case "removecoop":
                        //    await ContractCommands.RemoveCoop(message, args, db, _discord);
                        //    break;

                        //case "accept":
                        //    await RegisterCommands.Accept(message, args, db, _discord);
                        //    break;
                        //case "moveserver":
                        //    await RegisterCommands.MoveServer(message, args, db, _discord, _apiLink);
                        //    break;
                        //case "register":
                        //    await RegisterCommands.Register(message, args, db, _discord, _apiLink);
                        //    break;
                        //case "updateid":
                        //    await RegisterCommands.UpdateID(message, args, db, _discord, _apiLink);
                        //    break;
                    }
                }
            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await message.Channel.SendMessageAsync($"ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {message.Author.Mention}");
            }
        }

    }
}
