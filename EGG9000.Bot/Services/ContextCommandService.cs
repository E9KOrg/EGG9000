
//using Discord;
//using Discord.Net;
//using Discord.WebSocket;

//using EGG9000.Bot.Automated;
//using EGG9000.Bot.Commands;
//using EGG9000.Common.Database;
//using EGG9000.Common.Database.Entities;

//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.Hosting;

//using Newtonsoft.Json;

//using System;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Linq;
//using System.Reflection;
//using System.Text;
//using System.Threading;
//using System.Threading.Tasks;

//namespace EGG9000.Bot.Services {

//    }
//    public class ContextCommandService : IHostedService {
//        private readonly DiscordSocketClient _discord;
//        private List<ContextUserCommandFunction> _commandFunctions;
//        private IConfiguration _configuration;
//        private Bugsnag.IClient _bugsnag;
//        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(50);
//        private Guild _cpGuild;


//        public ContextCommandService(IConfiguration Configuration, DiscordSocketClient discord, Bugsnag.IClient bugsnag, ApplicationDbContext context) {
//            _discord = (DiscordSocketClient)discord;
//            _configuration = Configuration;
//            _bugsnag = bugsnag;
//            ulong.TryParse(Configuration.GetConnectionString("CPGuildId"), out ulong _CPGuildId);
//            _cpGuild = context.Guilds.FirstOrDefault(x => x.Id == _CPGuildId);
//        }

//        private async Task _discord_UserCommandExecuted(SocketUserCommand arg) {
//            try {
//                var command = _commandFunctions.First(x => x.Name == arg.Data.Name);

//                if(command.Details.AdminOnly) {
//                    var isAdmin = false;

//                    try {
//                        isAdmin = ((SocketGuildUser)arg.User).Roles.Any(x => x.Permissions.ManageChannels ||
//                            x.Name.ToLower().Contains("admin") || x.Name.ToLower().Contains("staff")) ||
//                            arg.User.Username == "kendrome" ||
//                            ((SocketGuildUser)arg.User).GuildPermissions.ManageChannels;
//                    } catch(Exception) {

//                    }
//                    if(!isAdmin) {
//                        if(command.Details.AllowFarmHand && ((SocketGuildUser)arg.User).Roles.Any(r => r.Name.ToLower().Contains("farm hand"))) {
//                            //bypass for farm hands and merits
//                        } else {
//                            await arg.RespondAsync($"{arg.User.Mention} You don't have permissions to run the command '/{arg.Data.Name}'");
//                            return;
//                        }
//                    }
//                }

//                _ = Task.Run(() => RunCommand(command, arg));
//            } catch(Exception e) {
//                _bugsnag.Notify(e);
//                var frame = (new StackTrace(e, true)).GetFrame(0);

//                await arg.RespondAsync($"ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

//            }
//            _semaphoreSlim.Release();
//        }

//        private async Task RunCommand(ContextUserCommandFunction command, SocketUserCommand arg) {
//            if(await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(5))) {
//                try {
//                    var parameters = new List<object>();
//                    foreach(var parameterInfo in command.Parameters) {
//                        if(parameterInfo.ParameterType == typeof(SocketUserCommand)) {
//                            parameters.Add(arg);
//                        }
//                        if(parameterInfo.ParameterType == typeof(ApplicationDbContext)) {
//                            parameters.Add(new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]));
//                        }
//                        if(parameterInfo.ParameterType == typeof(DiscordSocketClient)) {
//                            parameters.Add(_discord);
//                        }
//                        if(parameterInfo.ParameterType == typeof(SocketUser)) {
//                            parameters.Add(arg.User);
//                        }
//                    }

//                    await (Task)command.MethodInfo.Invoke(null, parameters.ToArray());
//                } catch(Exception e) {
//                    _bugsnag.Notify(e);
//                    var frame = (new StackTrace(e, true)).GetFrame(0);

//                    await arg.RespondAsync($"ERROR: Bot error - {e.Message.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

//                }
//            } else {
//                _bugsnag.Notify(new Exception("Slash Command Semaphore Limit Hit"));
//                await arg.RespondAsync("ERROR: Unable to run command at this time");
//            }
//        }

//        private async Task CreateCommands() {
//            //await _discord.Rest.DeleteAllGlobalCommandsAsync();
//            _discord.UserCommandExecuted += _discord_UserCommandExecuted;

//            Console.WriteLine("Creating context commands");
//            List<UserCommandProperties> applicationCommandProperties = new();

//            foreach(var command in _commandFunctions) {
//                var guildCommand = new UserCommandBuilder();
//                guildCommand.WithName(command.Details.Name ?? command.Name);

//                if(command.Details.AdminOnly) {
//                    guildCommand.DefaultMemberPermissions = GuildPermission.ManageChannels;
//                }
//            }

//            try {
//                foreach(var guild in _discord.Guilds) { 
//                    Console.WriteLine($"Creating context commands for {guild.Name}");
//                    var discordCommands = await guild.BulkOverwriteApplicationCommandAsync(applicationCommandProperties.ToArray());
//                }
//            } catch(Exception exception) {
//                var json = JsonConvert.SerializeObject(exception, Formatting.Indented);
//                Console.WriteLine(json);
//            }

//            Console.WriteLine("Context Commands Created");
//        }

//        public Task StartAsync(CancellationToken cancellationToken) {
//            _commandFunctions = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
//                      .SelectMany(t => t.GetMethods())
//                      .Where(m => m.GetCustomAttributes(typeof(ContextUserCommandAttribute), false).Length > 0)
//                      .Select(x => new ContextUserCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<ContextUserCommandAttribute>(), Parameters = x.GetParameters() })
//                      .ToList();


//            CreateCommands().ConfigureAwait(false);
//            return Task.CompletedTask;
//        }

//        public async Task StopAsync(CancellationToken cancellationToken) {
//            _discord.UserCommandExecuted += _discord_UserCommandExecuted;
//            Console.WriteLine($"Stopped listening to context commands");
//            if(_semaphoreSlim.CurrentCount > 0) {
//                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
//            }
//            await _semaphoreSlim.WaitAsync(cancellationToken);
//        }
//    }
//}
