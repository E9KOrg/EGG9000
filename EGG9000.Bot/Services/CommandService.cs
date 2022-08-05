
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.Automated;
using EGG9000.Bot.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Bot.Services.FauxCommand;

namespace EGG9000.Bot.Services {


    public class CommandService : IHostedService {
        private readonly DiscordHostedService _discord;
        private List<SlashCommandFunction> _slashCommandFunctions;
        private List<UserCommandFunction> _userCommandFunctions;
        private IConfiguration _configuration;
        private APILink _apilink;
        private Words _words;
        private Bugsnag.IClient _bugsnag;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(50);
        private ContractUpdater _contractUpdater;
        private CoopStatusUpdater _coopStatusUpdater;
        private Guild _cpGuild;


        public CommandService(IConfiguration Configuration, DiscordHostedService discord, APILink apilink, Words words, Bugsnag.IClient bugsnag, ContractUpdater contractUpdater, CoopStatusUpdater coopStatusUpdater, ApplicationDbContext context) {
            _discord = discord;
            _configuration = Configuration;
            _apilink = apilink;
            _words = words;


            _bugsnag = bugsnag;
            _contractUpdater = contractUpdater;
            _coopStatusUpdater = coopStatusUpdater;
            ulong.TryParse(Configuration.GetConnectionString("CPGuildId"), out ulong _CPGuildId);
            _cpGuild = context.Guilds.FirstOrDefault(x => x.Id == _CPGuildId);
        }

        private async Task _discord_SlashCommandExecuted(SocketSlashCommand arg) {
            try {
                var command = _slashCommandFunctions.First(x => x.Name == arg.Data.Name);

                if(command.Details.AdminOnly) {
                    var isAdmin = false;

                    try {
                        isAdmin = ((SocketGuildUser)arg.User).Roles.Any(x => x.Permissions.ManageChannels ||
                            x.Name.ToLower().Contains("admin") || x.Name.ToLower().Contains("staff")) ||
                            arg.User.Username == "kendrome" ||
                            ((SocketGuildUser)arg.User).GuildPermissions.ManageChannels;
                    } catch(Exception) {

                    }
                    if(!isAdmin) {
                        //if(command.Details.AllowFarmHand  && ((SocketGuildUser)arg.User).Roles.Any(r => r.Name.ToLower().Contains("farm hand"))) {
                            if((command.Details.AllowFarmHand || true) && ((SocketGuildUser)arg.User).Roles.Any(r => r.Name.ToLower().Contains("farm hand"))) {
                            //bypass for farm hands and merits
                        } else {
                            await arg.RespondAsync($"{arg.User.Mention} You don't have permissions to run the command '/{arg.Data.Name}'");
                            return;
                        }
                    }
                }

                if(command.SubFunctions != null) {
                    command = command.SubFunctions.First(x => x.Name == arg.Data.Options.First().Name);
                }
                _ = Task.Run(() => RunCommand(command, arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

            }
        }

        private async Task _discord_UserCommandExecuted(SocketUserCommand arg) {
            try {
                var command = _userCommandFunctions.First(x => x.Name == arg.Data.Name);

                _ = Task.Run(() => RunCommand(command, arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

            }
        }


        private async Task RunCommand(CommandFunctionBase command, IDiscordInteraction arg) {
            if(await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(5))) {
                try {
                    var parameters = new List<object>();
                    foreach(var parameterInfo in command.Parameters) {
                        if(parameterInfo.GetCustomAttributes<SlashParamAttribute>().Any()) {
                            parameters.Add(GetParam(parameterInfo, command, arg));
                            continue;
                        }

                        if(parameterInfo.ParameterType == typeof(FauxCommand)) {
                            if(arg is SocketSlashCommand)
                                parameters.Add(new FauxCommand(arg as SocketSlashCommand));
                            else
                                parameters.Add(arg);
                        }
                        if(parameterInfo.ParameterType == typeof(SocketUserCommand)) {
                            parameters.Add(arg);
                        }

                        if(parameterInfo.ParameterType == typeof(ApplicationDbContext)) {
                            parameters.Add(new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]));
                        }
                        if(parameterInfo.ParameterType == typeof(DiscordSocketClient)) {
                            parameters.Add((DiscordSocketClient)_discord);
                        }
                        if(parameterInfo.ParameterType == typeof(DiscordHostedService)) {
                            parameters.Add(_discord);
                        }
                        if(parameterInfo.ParameterType == typeof(APILink)) {
                            parameters.Add(_apilink);
                        }
                        if(parameterInfo.ParameterType == typeof(Words)) {
                            parameters.Add(_words);
                        }
                        if(parameterInfo.ParameterType == typeof(SocketUser)) {
                            parameters.Add(arg.User);
                        }
                        if(parameterInfo.ParameterType == typeof(CoopStatusUpdater)) {
                            parameters.Add(_coopStatusUpdater);
                        }
                        if(parameterInfo.ParameterType == typeof(ContractUpdater)) {
                            parameters.Add(_contractUpdater);
                        }
                    }

                    await (Task)command.MethodInfo.Invoke(null, parameters.ToArray());
                } catch(Exception e) {
                    _bugsnag.Notify(e);
                    var frame = (new StackTrace(e, true)).GetFrame(0);


                    if(arg.HasResponded) {
                        await arg.ModifyOriginalResponseAsync(msg => msg.Content = $"ERROR: Bot error - {e.Message.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

                    } else {
                        await arg.RespondAsync($"ERROR: Bot error - {e.Message.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");
                    }
                } finally {
                    _semaphoreSlim.Release();
                }
            } else {
                _bugsnag.Notify(new Exception("Command Semaphore Limit Hit"));
                await arg.RespondAsync("ERROR: Unable to run command at this time, please try again in a minute");
            }
        }


        private FauxSocketSlashCommandDataOption FindOption(string name, IList<FauxSocketSlashCommandDataOption> options) {
            var foundOption = options.FirstOrDefault(x => x.Name == name);
            if(foundOption != null) {
                return foundOption;
            }

            foreach(var option in options) {
                if(option.Options != null) {
                    foundOption = FindOption(name, option.Options.ToList());
                    if(foundOption != null) {
                        return foundOption;
                    }
                }
            }
            return null;
        }

        private async Task CreateCommands() {
            //await _discord.Rest.DeleteAllGlobalCommandsAsync();
            _discord.SlashCommandExecuted += _discord_SlashCommandExecuted;
            _discord.UserCommandExecuted += _discord_UserCommandExecuted;
            _discord.MessageReceived += _discord_MessageReceived;

            Console.WriteLine("Creating slash commands");
            List<ApplicationCommandProperties> applicationCommandProperties = new();
            List<ApplicationCommandProperties> cpApplicationCommandProperties = new();

            foreach(var command in _slashCommandFunctions) {
                var guildCommand = new SlashCommandBuilder();
                guildCommand.DefaultMemberPermissions = command.Details.AdminOnly ? GuildPermission.Administrator : GuildPermission.UseApplicationCommands;
                guildCommand.WithName(command.Name);
                if(command.Details.AdminOnly) {
                    command.Details.Description = $"(Admin Only) {command.Details.Description}";
                }
                guildCommand.WithDescription(command.Details.Description);
                //guildCommand.Description += "~";

                if(command.Details.AdminOnly) {
                    guildCommand.DefaultMemberPermissions = GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles;
                }

                if(command.SubFunctions != null) {
                    foreach(var subFunction in command.SubFunctions) {
                        var subCommandOption = new SlashCommandOptionBuilder();
                        subCommandOption.WithName(subFunction.Name);
                        subCommandOption.WithDescription(subFunction.Details.Description);
                        subCommandOption.WithType(ApplicationCommandOptionType.SubCommand);
                        guildCommand.AddOption(subCommandOption);
                        foreach(var parameterInfo in subFunction.Parameters.Where(x => x.GetCustomAttributes<SlashParamAttribute>().Any())) {
                            AddCommandParams(parameterInfo, null, subCommandOption);
                        }
                    }
                } else {
                    foreach(var parameterInfo in command.Parameters.Where(x => x.GetCustomAttributes<SlashParamAttribute>().Any())) {
                        AddCommandParams(parameterInfo, guildCommand, null);
                    }
                }

                if(!command.Details.CPOnly) {
                    applicationCommandProperties.Add(guildCommand.Build());
                }
                cpApplicationCommandProperties.Add(guildCommand.Build());
            }

            foreach(var command in _userCommandFunctions) {
                var guildCommand = new UserCommandBuilder();
                guildCommand.DefaultMemberPermissions = command.Details.AdminOnly ? GuildPermission.Administrator : GuildPermission.UseApplicationCommands;
                guildCommand.WithName(command.Details.Name ?? command.Name);
                command.Name = command.Details.Name ?? command.Name;

                if(command.Details.AdminOnly) {
                    guildCommand.DefaultMemberPermissions = GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles;
                }

                if(!command.Details.CPOnly) {
                    applicationCommandProperties.Add(guildCommand.Build());
                }
                cpApplicationCommandProperties.Add(guildCommand.Build());
            }

            try {
                foreach(var guild in _discord.Guilds) { 
                    Console.WriteLine($"Creating slash commands for {guild.Name}");

                    var isCPGuild = guild.Id == _cpGuild.Id || _cpGuild.OverflowServers.Contains(guild.Id);

                    var discordCommands = await guild.BulkOverwriteApplicationCommandAsync((isCPGuild ? cpApplicationCommandProperties : applicationCommandProperties).ToArray());
                }
            } catch(Exception exception) {
                var json = JsonConvert.SerializeObject(exception, Formatting.Indented);
                Console.WriteLine(json);
            }

            Console.WriteLine("Slash Commands Created");
        }

        private object GetParam(ParameterInfo parameterInfo, CommandFunctionBase command, IDiscordInteraction arg) {
            if(arg is FauxCommand) {
                return null;
            } else {
                var fauxCommand = arg is SocketSlashCommand ? new FauxCommand(arg as SocketSlashCommand) : arg as FauxCommand;
                var slashParamDetails = parameterInfo.GetCustomAttribute<SlashParamAttribute>();
                var name = parameterInfo.Name.ToLower();
                if(parameterInfo.ParameterType == typeof(SocketGuildUser[])) {
                    var users = new List<SocketGuildUser>();
                    for(var i = 1; i <= 10; i++) {
                        var option = FindOption($"{name}{i}", fauxCommand.Data.Options);
                        if(option != null) {
                            users.Add((SocketGuildUser)option.Value);
                        }
                    }
                    return users.ToArray();
                }

                var optionResult = FindOption(name, fauxCommand.Data.Options);
                if(optionResult == null) {
                    return null;
                }
                if(parameterInfo.ParameterType == typeof(int)) {
                    return Convert.ToInt32((Int64)FindOption(name, fauxCommand.Data.Options)?.Value);
                } else {
                    return FindOption(name, fauxCommand.Data.Options)?.Value;
                }
            }
        }
        private async Task _discord_MessageReceived(SocketMessage message) {
            var db = new ApplicationDbContext(_configuration["ConnectionStrings:DefaultConnection"]);
            var guild = message.Channel is SocketGuildChannel ? (message.Channel as SocketGuildChannel).Guild : null;
            if(((IMessage)message).Type == MessageType.UserPremiumGuildSubscription && guild.Id == _cpGuild.Id) {
                var cpGeneralChannel = guild.TextChannels.First(x => x.Id == 656455568353132546);
                await MeritCommands.CreateMerit("Boosted the server!", db, _discord, message.Author, Guid.Empty, cpGeneralChannel);
                await cpGeneralChannel.SendMessageAsync($"{message.Author.Mention} just boosted the server!");
            }
            if(message.Content.StartsWith("/") && (message.Interaction is null || message.Interaction.Type != InteractionType.ApplicationCommand)) {
                var commandText = new Regex(@"^/(\w+)").Match(message.Content).Groups[1].Value.ToLower();
                var command = _slashCommandFunctions.FirstOrDefault(x => x.Name == commandText);
                if(command != null) {
                    await RunCommand(command, new FauxCommand(message, guild.Id));
                }
            }
        }


        private void AddCommandParams(ParameterInfo parameterInfo, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null) {
            var slashParamDetails = parameterInfo.GetCustomAttribute<SlashParamAttribute>();
            var name = parameterInfo.Name.ToLower();
            if(string.IsNullOrEmpty(slashParamDetails.Description)) {
                slashParamDetails.Description = name;
            }

            var types = new Dictionary<Type, ApplicationCommandOptionType> {
                        {typeof(int), ApplicationCommandOptionType.Integer },
                        {typeof(string), ApplicationCommandOptionType.String },
                        {typeof(bool), ApplicationCommandOptionType.Boolean },
                        {typeof(SocketGuildUser), ApplicationCommandOptionType.User },
                        {typeof(SocketUser), ApplicationCommandOptionType.User },
                        {typeof(SocketChannel), ApplicationCommandOptionType.Channel },
                        {typeof(SocketRole), ApplicationCommandOptionType.Role },
                    };

            if(types.Any(x => x.Key == parameterInfo.ParameterType)) {
                AddOption(name, types.First(x => x.Key == parameterInfo.ParameterType).Value, description: slashParamDetails.Description, isRequired: slashParamDetails.Required, guildCommand, subCommand);
                return;
            }
            if(parameterInfo.ParameterType == typeof(SocketGuildUser[])) {
                for(var i = 1; i <= 10; i++) {
                    AddOption($"{name}{i}", ApplicationCommandOptionType.User, description: $"{slashParamDetails.Description} {i}", isRequired: i > 1 ? false : slashParamDetails.Required, guildCommand, subCommand);
                }
                return;
            }
            throw new NotImplementedException($"Parameter not implemented for {parameterInfo.Name} of type {parameterInfo.ParameterType}");
        }

        private void AddOption(String name, ApplicationCommandOptionType type, string description, bool isRequired, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null) {
            if(guildCommand != null) {
                guildCommand.AddOption(name, type, description, isRequired);
            } else {
                subCommand.AddOption(name, type, description, isRequired);
            }

        }

        public Task StartAsync(CancellationToken cancellationToken) {
            var slashCommands = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(SlashCommandAttribute), false).Length > 0)
                      .Select(x => new SlashCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<SlashCommandAttribute>(), Parameters = x.GetParameters() })
                      .ToList();



            _slashCommandFunctions = new List<SlashCommandFunction>();
            _slashCommandFunctions.AddRange(slashCommands.Where(x => string.IsNullOrWhiteSpace(x.Details.ParentCommand)));
            _slashCommandFunctions.AddRange(
                slashCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Details.ParentCommand))
                    .GroupBy(x => x.Details.ParentCommand)
                    .Select(x => new SlashCommandFunction {
                        Name = x.Key,
                        SubFunctions = x.ToList(),
                        Details = new SlashCommandAttribute { Description = "", AdminOnly = x.Any(y => y.Details.AdminOnly) }
                    })
                );

            var t = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(UserCommandAttribute), false).Length > 0);
            _userCommandFunctions = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(UserCommandAttribute), false).Length > 0)
                      .Select(x => new UserCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<UserCommandAttribute>(), Parameters = x.GetParameters() })
                      .ToList();
            CreateCommands().ConfigureAwait(false);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _discord.SlashCommandExecuted -= _discord_SlashCommandExecuted;
            Console.WriteLine($"Stopped listening to slash commands");
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }
    }


}
