using Discord;
using Discord.WebSocket;
using DiscordCoopCodes;
using DiscordCoopCodes.Automated;
using DiscordCoopCodes.Commands;
using DiscordCoopCodes.Database;
using DiscordCoopCodes.Database.Entities;
using DiscordCoopCodes.EggIncAPI;
using DiscordCoopCodes.Helpers;
using EGG9000.Common.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DiscordCoopCodes.Services;
using Bugsnag.AspNet.Core;
using Discord.Rest;
using System.Threading;
using static EGG9000.Common.Helpers.Prefarm;
using EGG9000.Common.Database;
using System.Diagnostics;

namespace DiscordCoopCords {
    class Program {
        private static IConfigurationRoot Configuration;
        private static DiscordSocketClient client;
        private static DiscordRestClient restclient;
        private static Words _words;
        private static IMemoryCache _cache;
        private static APILink _apiLink;
        private static Bugsnag.IClient _bugsnag;

        private static List<DBUser> users;

        static void Main(string[] args) {
            CreateHostBuilder(args).Build().Run();
            //new Program().MainAsync().GetAwaiter().GetResult();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices((hostContext, services) => {


                    //var response = ContractsAPI.Post<Ei.CreateCoopResponse, Ei.CreateCoopRequest>(new Ei.CreateCoopRequest {
                    //    ClientVersion = 30,
                    //    ContractIdentifier = "mday-brunch",
                    //    CoopIdentifier = "test2",
                    //    League = 0,
                    //    Platform = Aux.Platform.Ios,
                    //    //SecondsRemaining = Math.Max(guildContract.Contract.Details.LengthSeconds, 131072),
                    //    SecondsRemaining = 131071,
                    //    SoulPower = 24.24559831915049,
                    //    UserId = ContractsAPI.UserId,
                    //    UserName = "EK9"
                    //}, ContractsAPI.UserId);

                    //response.Wait();


                    //var r = ContractsAPI.Send<Ei.LeaveCoopRequest>(new Ei.LeaveCoopRequest {
                    //    ClientVersion = ContractsAPI.ClientVersion,
                    //    ContractIdentifier = "boba-shortage-2021",
                    //    CoopIdentifier = "DrankSmell50".ToLower(),
                    //    PlayerIdentifier = "EI6571815536689152"
                    //}, "EI6571815536689152");
                    //r.Wait();


                    //var r = ContractsAPI.Send<Ei.KickPlayerCoopRequest>(new Ei.KickPlayerCoopRequest {
                    //     ClientVersion = ContractsAPI.ClientVersion, ContractIdentifier = "boba-shortage-2021", CoopIdentifier = "nextcrate41 ", PlayerIdentifier = "EI5223299518300160", Reason = Ei.KickPlayerCoopRequest.Types.Reason.Private, RequestingUserId = "EI5223299518300160"
                    //}, "EI5223299518300160");
                    //r.Wait();


                    Console.WriteLine("Main Start");

                    _bugsnag = new Bugsnag.Client(new Bugsnag.Configuration("c924bd8a1fd56db4552e0549a76d3689"));
                    services.AddSingleton<Bugsnag.IClient>(_bugsnag);
                    //services.AddBugsnag(configuration => {
                    //    configuration.ApiKey = "c924bd8a1fd56db4552e0549a76d3689";
                    //});

                    Configuration = new ConfigurationBuilder()
                        .AddUserSecrets<Secrets>()
                        .Build();

                    client = new DiscordSocketClient();
                    restclient = new DiscordRestClient();
                    _cache = new MemoryCache(new MemoryCacheOptions { });

                    var db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);

                    _apiLink = new APILink(_cache);







                    Console.WriteLine("Getting User Backups for Cache");
                    var usersTask = db.DBUsers.AsQueryable().Where(x => x.GuildId > 0).ToListAsync();
                    usersTask.Wait();
                    var backups = usersTask.Result.SelectMany(x => x.Backups ?? new List<EGG9000.Common.Database.CustomBackup>());
                    if(backups != null) {
                        _apiLink.AddExistingBackups(backups);
                    }



                    client.Log += Log;



                    client.LoginAsync(TokenType.Bot, Configuration["Token"]).Wait();
                    client.StartAsync().Wait();

                    restclient.LoginAsync(TokenType.Bot, Configuration["Token"]).Wait();


                    Console.WriteLine("Waiting on Discord Connect");

                    while(client.ConnectionState != ConnectionState.Connected) { }





                    client.SetGameAsync("!help").Wait();

                    services.AddDbContext<ApplicationDbContext>(options =>
                        options.UseSqlServer(
                            Configuration.GetConnectionString("DefaultConnection")));

                    services.AddHostedService<DatabaseQueue>();
                    services.AddSingleton(_apiLink);
                    _words = new Words();
                    services.AddSingleton(_words);
                    services.AddSingleton(client);

                    ////services.AddSingleton<LeaderboardUpdater>();

                    services.AddHostedService<StaffCoopsMessage>();
                    services.AddHostedService<EventUpdater>();
                    services.AddHostedService<CoopReorder>();
                    services.AddHostedService<CoopDeleteChannel>();
                    services.AddHostedService<CoopStatusUpdater>();
                    services.AddHostedService<ContractUpdater>();
                    services.AddHostedService<NewContracts>();
                    services.AddHostedService<CreateCoopChannels>();
                    services.AddHostedService<ShipReturnDM>();
                    services.AddHostedService<UserSnapShots>();
                    services.AddHostedService<LeaderboardUpdater>();
                    services.AddHostedService<ManageOverflow>();

                    client.MessageReceived += MessageReceived;
                    client.UserJoined += Client_UserJoined;
                    client.UserLeft += Client_UserLeft;

                }).ConfigureAppConfiguration((context, config) => {
                    // configure the app here.

                });


        private static async Task Client_UserJoined(SocketGuildUser user) {
            Client_UserJoined_Task(user).ConfigureAwait(false);
        }

        private static async Task Client_UserJoined_Task(SocketGuildUser user) {
            var db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);
            await RegisterCommands.UserJoined(user, db);
        }

        private static async Task Client_UserLeft(SocketGuildUser user) {
            Client_UserLeft_Task(user).ConfigureAwait(false);
        }

        private static async Task Client_UserLeft_Task(SocketGuildUser user) {
            var db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);
            await RegisterCommands.UserLeft(user, db);
        }

        private static Task Log(LogMessage msg) {
            if(!msg.ToString().Contains("Rate limit triggered")) {
                Console.WriteLine(msg.ToString());
            }
            return Task.CompletedTask;
        }

        private static async Task MessageReceived(SocketMessage message) {
            MessageReceivedTask(message).ConfigureAwait(false);
        }

        private static async Task MessageReceivedTask(SocketMessage message) {
            var db = new ApplicationDbContext(Configuration["ConnectionStrings:DefaultConnection"]);
            if(((IMessage)message).Type == MessageType.UserPremiumGuildSubscription) {
                if(message.Channel.Id == 680431628950044676) { //CP Welcome Channel
                    var cpGeneralChannel = client.Guilds.First(x => x.Id == 656455567858073601).TextChannels.First(x => x.Id == 656455568353132546);
                    await MeritCommands.CreateMerit(message, "Boosted the server!", db, client, message.Author, Guid.Empty, cpGeneralChannel);
                }
                return;
            }
            try {
                //if(message.Content.StartsWith("*testemoji")) {
                //    var args = message.Content.Split(' ').Skip(1).ToArray();
                //    await MiscCommands.TestEmoji(message, args);
                //}
                //return;



                if(message.Content.StartsWith("!")) {
                    Console.WriteLine($"Message: {message}");
                    var command = message.Content.Substring(1).Split(' ')[0].ToLower();
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
                        switch(command.Replace("-", "")) {
                            case "say":
                                await RegisterCommands.Say(message, args);
                                return;
                            case "clean":
                                await RegisterCommands.Clean(message, client);
                                return;
                            //case "test-event":
                            //    await _eventUpdater.TestEvent(message, args);
                            //    return;
                            case "makepublic":
                                await ContractCommands.MakePublic(message, db);
                                return;
                            case "makeprivate":
                                await ContractCommands.MakePrivate(message, db);
                                return;
                            case "adddemerit":
                                await DemeritCommands.AddDemerit(message, args, db);
                                return;
                            case "removedemerit":
                                await DemeritCommands.RemoveDemerit(message, args, db);
                                return;
                            case "testadmin":
                                await message.Channel.SendMessageAsync($"You are an admin!");
                                return;
                            case "start":
                                await ContractCommands.Start(message, args, db, client, _apiLink, _words);
                                return;
                            case "setnumber":
                                await ContractCommands.SetNumber(message, args, db, client);
                                return;
                            case "update": {
                                var channel = (SocketTextChannel)message.Channel;
                                if(channel.Category?.Name.ToLower().Contains("contracts") ?? false) {
                                    await ContractCommands.Update(message, args, db, client);
                                } else if(channel.Name.ToLower().Contains("leaderboard")) {
                                    await ContractCommands.Update(message, args, db, client);
                                    //_leaderboardUpdater.Update();
                                } else {
                                    await message.Channel.SendMessageAsync($"This command only works in a contract or leaderboard channel. {channel.Category.Name}");
                                }
                                return;
                            }
                            case "missingregistrations":
                                await MissingRegistrations.Run(message, db, client);
                                return;
                            case "newcode":
                                await NewCode.ExecuteAsync(message, db, client);
                                return;
                            case "removenull":
                                await RegisterCommands.RemoveNull(message, args, db, client, _apiLink);
                                return;
                            case "showduplicates":
                                await RegisterCommands.ShowDuplicates(message, args, db, client);
                                return;
                            case "removeduplicates":
                                await RegisterCommands.RemoveDuplicates(message, args, db, client);
                                return;
                            case "delete": {
                                var channel = (SocketTextChannel)message.Channel;
                                if(channel.Category.Name.ToLower().Contains("contracts")) {
                                    await ContractCommands.Delete(message, args, db, client);
                                    return;
                                } else if(channel.Category.Name.ToLower().Contains("co-op") || channel.Category.Name.ToLower().Contains("coop")) {
                                    await NewCode.DeleteCoop(message, db, client);
                                    return;
                                    //else if (channel.Category.Name.ToLower().Contains("coop") || channel.Category.Name.ToLower().Contains("co-op"))
                                } else {
                                    await message.Channel.SendMessageAsync($"This command only works in a contract or co-op channel. {channel.Category.Name}");
                                }
                                return;
                            }
                            case "move":
                                await ContractCommands.Move(message, args, db, client);
                                return;
                            case "remove":
                                await ContractCommands.Remove(message, args, db, client);
                                return;
                            case "nodemerit":
                                await DemeritCommands.NoDemerit(message, args, db);
                                return;
                            case "removename":
                                await RegisterCommands.RemoveEggName(message, args, db, client, _apiLink);
                                return;
                            case "removeid":
                                await RegisterCommands.RemoveID(message, args, db, client, _apiLink);
                                return;
                            case "leavecoop":
                                await RegisterCommands.LeaveCoop(message, args, db, client);
                                return;
                            case "addprefarmers":
                                await ContractCommands.AddPrefarmers(message, args, db, client, _apiLink);
                                return;
                            case "disable":
                                await RegisterCommands.Disable(message, args, db, client);
                                return;
                            case "enable":
                                await RegisterCommands.Enable(message, args, db, client);
                                return;
                            case "fixreference":
                                await ContractCommands.FixReference(message, args, db);
                                return;
                            case "checkroles": {
                                var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
                                await guild.DownloadUsersAsync();
                                var count = guild.Users.Count(u => message.MentionedRoles.All(r => u.Roles.Any(x => x.Id == r.Id)));
                                await message.Channel.SendMessageAsync($"{count} users with the roles {String.Join(", ", message.MentionedRoles.Select(x => x.Mention))}");
                                return;
                            }
                            case "listusers": {
                                var guild = client.Guilds.FirstOrDefault(x => x.TextChannels.Any(y => y.Id == message.Channel.Id));
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
                            //case "staffcoops":
                            //    await MiscCommands.StaffCoops(message, args, db, client);
                            //    return;

                        }
                    } else {
                        switch(command) {
                            case "testevent":
                            case "cleanwelcome":
                            case "cleanunpinned":
                            case "makepublic":
                            case "testadmin":
                            case "delete":
                            case "start":
                            case "start100":
                            case "setnumber":
                            case "missingregistrations":
                            case "newcode":
                            case "removenull":
                            case "showduplicates":
                            case "move":
                            case "remove":
                            case "update":
                            case "removeduplicates":
                            case "kick":
                            case "enable":
                            case "disable":
                            case "removename":
                                await message.Channel.SendMessageAsync($"{message.Author.Mention} You don't have permissions to run the command '!{command}'");
                                return;
                        }

                    }


                    //Farm Hands
                    var isFarmHand = false;
                    try {
                        isFarmHand = ((SocketGuildUser)message.Author).Roles.Any(x =>
                            x.Name.ToLower().Contains("farm hand"));
                    } catch(Exception) {

                    }

                    if(isAdmin || isFarmHand) {
                        switch(command) {
                            case "addmerit":
                                await MeritCommands.AddMerit(message, args, db, client);
                                return;
                            case "removemerit":
                                await MeritCommands.RemoveMerit(message, args, db);
                                return;

                        }
                    } else {
                        switch(command) {
                            case "addmerit":
                            case "removemerit":
                                await message.Channel.SendMessageAsync($"{message.Author.Mention} You don't have permissions to run the command '!{command}'");
                                return;

                        }
                    }


                    if(!isAdmin && message.MentionedUsers.Count > 0) {
                        await message.Channel.SendMessageAsync($"{message.Author.Mention} You don't have permissions to @mention a user when using the command !{command}");
                        return;
                    }
                    switch(command) {
                        case "userstatus":
                            await RegisterCommands.userstatus(message, args, db, client, _apiLink);
                            return;
                        case "showeb":
                            await RegisterCommands.ShowEB(message, args, db);
                            return;
                        case "hideeb":
                            await RegisterCommands.HideEB(message, args, db);
                            return;
                        case "takeabreak":
                            await RegisterCommands.TakeABreak(message, args, db, client);
                            return;
                        case "skipnope":
                            await ContractCommands.SkipNoPe(message, args, db, client);
                            return;
                        case "unskipnope":
                            await ContractCommands.UnSkipNoPe(message, args, db, client);
                            return;
                        case "skip":
                            await ContractCommands.Skip(message, args, db, client);
                            return;
                        case "demerits":
                            await DemeritCommands.Demerits(message, args, db);
                            return;
                        case "merits":
                            await MeritCommands.Merits(message, args, db);
                            return;
                        //case "starter": await _contractUpdater.Starter(message, args, db, client); break;
                        //case "removestarter": await _contractUpdater.RemoveStarter(message, args, db, client); break;

                        //case "opencoops": await Contracts.OpenCoops(message, db); break; //**Need to update for multiple guilds

                        case "addcoop":
                            await ContractCommands.AddCoop(message, args, db, client, _apiLink);
                            break;
                        //case "mystatus": await ContractCommands.MyStatus(message, db); break;
                        //case "getstatus": await ContractCommands.GetStatus(message, args, db); break;
                        case "removecoop":
                            await ContractCommands.RemoveCoop(message, args, db, client);
                            break;

                        case "accept":
                            await RegisterCommands.Accept(message, args, db, client);
                            break;
                        case "moveserver":
                            await RegisterCommands.MoveServer(message, args, db, client, _apiLink);
                            break;
                        case "register":
                            await RegisterCommands.Register(message, args, db, client, _apiLink);
                            break;
                        case "updateid":
                            await RegisterCommands.UpdateID(message, args, db, client, _apiLink);
                            break;

                        case "help":
                            var helpMessage = @"Available Commands:
**!register {eggincid}** Registers your discord user with your EggInc ID, you can do this more than once for each EggInc account you have
**!addcoop {coopname}** Allows you to add an external coop that wasn't created with !newcode
**!demerits** List a users demerits
**!skip #contract-channel** Allows the user to opt out of that contract
**!skipNoPe  Allows the user to opt out of contracts with no <:Egg_of_Prophecy_PE:669981330477547580>
**!unSkipNoPe  Undos the !skipnope command
**!skip #contract-channel** Allows the user to opt out of that contract
**!takeabreak** Bot will not ping you about not pre-farming, this status will stay until the next time you start pre-farming
**!userstatus** Gives your status and last backup time
";
                            if(isAdmin) {
                                helpMessage += @"

Admin Only Commands:
**!start** starts the automatted coop creation (Contract Channel Only)
**!move @user @channel** Moves a user from one assigned co-op to another, only works if they haven't joined
**!setnumber {numberOfCoops}** Sets the number of coops to create (Admin Contract Channel Only)
**!newcode** Will give a new code for starting a contract (Only as a backup when automatted fails)
**!delete** Deletes a co-op or contract channel and stops updating
**!add-demerit @user reason** Adds a demerit to user for the given reason
**!remove-demerit @user** Removes the last demerit from user
**!makepublic** Makes a co-op public
**!cleanwelcome** Kicks users inactive 14 days and without a role. Then cleans up welcome channel.
**!cleanunpinned** Removes any unpinned messages from the channel
**!userstatus @user** Shows account details
**!disable** You will not be assigned co-ops until you run !enable
**!enable** Undo the !disable command so you can participate again
**!fixreference @user EggIncName** Fixes when someone shows as a alien but has been assigned a co-op
**!staffcoops** List co-ops for staff
**!listusers @role ... @role #channel** Will tag everyone with the combination of roles, optional tag another channel.
";
                            }

                            while(helpMessage.Length > 2000) {
                                var index = helpMessage.LastIndexOf('\n', 2000);
                                await message.Channel.SendMessageAsync(helpMessage.Substring(0, index));
                                helpMessage = helpMessage.Substring(index);
                            }


                            await message.Channel.SendMessageAsync(helpMessage);

                            /*                      
                            **!addname {{eggincname}}** Registers your discord user with your EggInc name, you can do this more than once for each EggInc account you have
                            **!removename {{eggincname}}** Allows you to remove a name that is wrong
                            **!mystatus** Will give you updates of all your coops that aren't done
                            **!opencoops** Will show you all contracts with open spots
                            */
                            break;
                        case "test": {
                            await ContractCommands.Test(message, db);
                        }
                        break;
                        default:
                            await message.Channel.SendMessageAsync(BotText.UnknownCommand(command) + $" {message.Author.Mention}");
                            break;
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
