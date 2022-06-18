
using Discord;
using Discord.Net;
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
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {

    public class CommandFunction {
        public MethodInfo MethodInfo { get; set; }
        public SlashCommandAttribute Details { get; set; }
        public ParameterInfo[] Parameters { get; set; }
        public string Name { get; set; }
        public List<CommandFunction> SubFunctions { get; set; }
    }
    public class SlashCommandService : IHostedService {
        private readonly DiscordHostedService _discord;
        private List<CommandFunction> _commandFunctions;
        private IConfiguration _configuration;
        private APILink _apilink;
        private Words _words;
        private Bugsnag.IClient _bugsnag;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(50);
        private ContractUpdater _contractUpdater;
        private CoopStatusUpdater _coopStatusUpdater;
        private Guild _cpGuild;


        public SlashCommandService(IConfiguration Configuration, DiscordHostedService discord, APILink apilink, Words words, Bugsnag.IClient bugsnag, ContractUpdater contractUpdater, CoopStatusUpdater coopStatusUpdater, ApplicationDbContext context) {
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
                var command = _commandFunctions.First(x => x.Name == arg.Data.Name);

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
                        if(command.Details.AllowFarmHand && ((SocketGuildUser)arg.User).Roles.Any(r => r.Name.ToLower().Contains("farm hand"))) {
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
                _ = Task.Run(() => CleanFailedMessages(arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

            }
            _semaphoreSlim.Release();
        }

        private async Task CleanFailedMessages(SocketSlashCommand arg) {
            var channel = arg.Channel;
            var messages = await channel.GetMessagesAsync(100).FlattenAsync();
            var failedMessages = messages.Where(x => x.Content.StartsWith($"/{arg.CommandName}", StringComparison.CurrentCultureIgnoreCase) && x.Author.Id == arg.User.Id).ToList();
            foreach(var failedMessage in failedMessages) {
                var responses = messages.Where(x => x.Reference != null && x.Reference.MessageId.IsSpecified && x.Reference.MessageId.Value == failedMessage.Id).ToList();
                foreach(var response in responses) {
                    await response.DeleteAsync();
                    await Task.Delay(540);
                }
                await failedMessage.DeleteAsync();
                await Task.Delay(540);
            }
        }

        private async Task RunCommand(CommandFunction command, SocketSlashCommand arg) {
            if(await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(5))) {
                try {
                    var parameters = new List<object>();
                    foreach(var parameterInfo in command.Parameters) {
                        if(parameterInfo.GetCustomAttributes<SlashParamAttribute>().Any()) {
                            var slashParamDetails = parameterInfo.GetCustomAttribute<SlashParamAttribute>();
                            var name = parameterInfo.Name.ToLower();
                            if(parameterInfo.ParameterType == typeof(SocketGuildUser[])) {
                                var users = new List<SocketGuildUser>();
                                for(var i = 1; i <= 10; i++) {
                                    var option = FindOption($"{name}{i}", arg.Data.Options);
                                    if(option != null) {
                                        users.Add((SocketGuildUser)option.Value);
                                    }
                                }
                                parameters.Add(users.ToArray());
                                continue;
                            }

                            var optionResult = FindOption(name, arg.Data.Options);
                            if(optionResult == null) {
                                parameters.Add(null);
                                continue;
                            }
                            if(parameterInfo.ParameterType == typeof(int)) {
                                parameters.Add(Convert.ToInt32((Int64)FindOption(name, arg.Data.Options)?.Value));
                            } else {
                                parameters.Add(FindOption(name, arg.Data.Options)?.Value);
                            }
                            continue;
                        }

                        if(parameterInfo.ParameterType == typeof(SocketSlashCommand)) {
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

                }
            } else {
                _bugsnag.Notify(new Exception("Slash Command Semaphore Limit Hit"));
                await arg.RespondAsync("ERROR: Unable to run command at this time");
            }
        }

        private  SocketSlashCommandDataOption FindOption(string name, IReadOnlyCollection<SocketSlashCommandDataOption> options) {
            var foundOption = options.FirstOrDefault(x => x.Name == name);
            if(foundOption != null) {
                return foundOption;
            }

            foreach(var option in options) {
                if(option.Options != null) {
                    foundOption = FindOption(name, option.Options);
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

            Console.WriteLine("Creating slash commands");
            List<ApplicationCommandProperties> applicationCommandProperties = new();
            List<ApplicationCommandProperties> cpApplicationCommandProperties = new();

            foreach(var command in _commandFunctions) {
                var guildCommand = new SlashCommandBuilder();
                guildCommand.DefaultMemberPermissions = command.Details.AdminOnly ? GuildPermission.Administrator : GuildPermission.UseApplicationCommands;
                guildCommand.WithName(command.Name);
                if(command.Details.AdminOnly) {
                    command.Details.Description = $"(Admin Only) {command.Details.Description}";
                }
                guildCommand.WithDescription(command.Details.Description);
                //guildCommand.Description += "~";

                if(command.Details.AdminOnly) {
                    guildCommand.IsDefaultPermission = false;
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

            try {
                foreach(var guild in _discord.Guilds) { //.Where(x => x.Id == 656455567858073601)
                    //await _discord.BulkOverwriteGlobalApplicationCommandsAsync(applicationCommandProperties.ToArray());
                    Console.WriteLine($"Creating slash commands for {guild.Name}");


                    var isCPGuild = guild.Id == _cpGuild.Id || _cpGuild.OverflowServers.Contains(guild.Id);

                    var discordCommands = await guild.BulkOverwriteApplicationCommandAsync((isCPGuild ? cpApplicationCommandProperties : applicationCommandProperties).ToArray());
                    IDictionary<ulong, ApplicationCommandPermission[]> permissions = new Dictionary<ulong, ApplicationCommandPermission[]>();

                    var adminRoles = guild.Roles
                        .Where(x => x.Permissions.ManageChannels || x.Name.ToLower().Contains("admin") || x.Name.ToLower().Contains("staff"))
                        .Select(r => new ApplicationCommandPermission(r, true));

                    var roles = guild.Roles
                        .Where(x => x.Permissions.ManageChannels || x.Name.ToLower().Contains("admin") || x.Name.ToLower().Contains("staff"));

                    foreach(var discordCommand in discordCommands) {
                        var command = _commandFunctions.First(x => x.Name == discordCommand.Name);
                        if(command.Details.AdminOnly) {
                            var rolesToAdd = adminRoles.ToList();
                            if(command.Details.AllowFarmHand) {
                                rolesToAdd.AddRange(guild.Roles.Where(x => x.Name.ToLower().Contains("farm hand")).Select(r => new ApplicationCommandPermission(r, true)));
                            }
                            permissions.Add(discordCommand.Id, rolesToAdd.ToArray());
                        }
                    }
                    if(permissions.Count > 0) {
                        //var commands = await guild.GetApplicationCommandsAsync();
                        //var c2 = await _discord.Rest.GetGuildApplicationCommands(1);
                        //c2.First().ModifyCommandPermissions
                        //var result = await _discord.Rest.BatchEditGuildCommandPermissions(guild.Id, permissions);
                        //Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
                    }
                    Console.WriteLine($"Slash command permissions updated for {guild.Name}");
                }
            } catch(Exception exception) {
                var json = JsonConvert.SerializeObject(exception, Formatting.Indented);
                Console.WriteLine(json);
            }

            Console.WriteLine("Slash Commands Created");
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
            throw new NotImplementedException($"Paramter not implemented for {parameterInfo.Name} of type {parameterInfo.ParameterType}");
        }

        private void AddOption(String name, ApplicationCommandOptionType type, string description, bool isRequired, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null) {
            if(guildCommand != null) {
                guildCommand.AddOption(name, type, description, isRequired);
            } else {
                subCommand.AddOption(name, type, description, isRequired);
            }

        }

        public Task StartAsync(CancellationToken cancellationToken) {
            var allCommands = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(SlashCommandAttribute), false).Length > 0)
                      .Select(x => new CommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<SlashCommandAttribute>(), Parameters = x.GetParameters() })
                      .ToList();
            _commandFunctions = new List<CommandFunction>();
            _commandFunctions.AddRange(allCommands.Where(x => string.IsNullOrWhiteSpace(x.Details.ParentCommand)));
            _commandFunctions.AddRange(
                allCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Details.ParentCommand))
                    .GroupBy(x => x.Details.ParentCommand)
                    .Select(x => new CommandFunction {
                        Name = x.Key,
                        SubFunctions = x.ToList(),
                        Details = new SlashCommandAttribute { Description = "", AdminOnly = x.Any(y => y.Details.AdminOnly) }
                    })
                );

            CreateCommands().ConfigureAwait(false);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _discord.SlashCommandExecuted -= _discord_SlashCommandExecuted;
            Console.WriteLine($"Stopeed listined to slash commands");
            if(_semaphoreSlim.CurrentCount > 0) {
                Console.WriteLine($"Waiting on {this.GetType().Name} to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }
    }
}
