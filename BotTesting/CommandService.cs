
using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

using EGG9000.Bot.Commands;
using EGG9000.Common.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Factories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

using static EGG9000.Common.Services.FauxCommand;

namespace EGG9000.Common.Services {


    public class CommandService : IHostedService {
        private readonly DiscordHostedService _discord;
        private List<SlashCommandFunction> _slashCommandFunctions;
        private List<UserCommandFunction> _userCommandFunctions;
        private List<ComponentCommandFunction> _componentCommandFunctions;
        private List<ModalCommandFunction> _modalFunctions;
        private IConfiguration _configuration;
        private Bugsnag.IClient _bugsnag;
        private SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(100);
        private Guild _cpGuild;
        private IServiceProvider _provider;
        private ILogger<CommandService> _logger;
        private List<(SocketApplicationCommand command, ulong guildid)> _discordCommands = new List<(SocketApplicationCommand command, ulong guildid)>();

        public CommandService(IConfiguration Configuration, DiscordHostedService discord,Bugsnag.IClient bugsnag, ApplicationDbContext context, IServiceProvider serviceProvider, ILogger<CommandService> logger) {
            _discord = discord;
            _configuration = Configuration;


            _bugsnag = bugsnag;
            ulong.TryParse(Configuration.GetConnectionString("CPGuildId"), out ulong _CPGuildId);
            _cpGuild = context.Guilds.FirstOrDefault(x => x.Id == _CPGuildId);
            _provider = serviceProvider;
            _logger = logger;
        }

        private async Task _discord_SlashCommandExecuted(SocketSlashCommand arg) {
            try {
                var command = _slashCommandFunctions.First(x => x.Name == arg.Data.Name);


                if(command.SubFunctions != null) {
                    command = command.SubFunctions.First(x => x.Name == arg.Data.Options.First().Name);
                }
                _ = Task.Run(() => RunCommand(command, arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

            }
        }

        private async Task _discord_UserCommandExecuted(SocketUserCommand arg) {
            try {
                var command = _userCommandFunctions.First(x => x.Name == arg.Data.Name);

                _ = Task.Run(() => RunCommand(command, arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

            }
        }


        private async Task RunCommand(CommandFunctionBase command, IDiscordInteraction arg) {
            if(await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(2.8))) {
                try {
                    var parameters = new List<object>();
                    foreach(var parameterInfo in command.Parameters) {
                        if(parameterInfo.GetCustomAttributes<SlashParamAttribute>().Any()) {
                            parameters.Add(GetParam(parameterInfo, command, arg));
                            continue;
                        }
                        if(parameterInfo.GetCustomAttributes<ComponentDataAttribute>().Any()) {
                            var data = ((SocketMessageComponent)arg).Data.CustomId.Split(":");
                            parameters.Add(data.Length > 1 ? (object)data[1] : null);
                            continue;
                        }

                        if(parameterInfo.ParameterType == typeof(FauxCommand)) {
                            if(arg is SocketSlashCommand)
                                parameters.Add(new FauxCommand(arg as SocketSlashCommand));
                            else
                                parameters.Add(arg);
                        } else if(parameterInfo.ParameterType == typeof(SocketMessageComponent)) {
                            parameters.Add(arg);
                        } else if(parameterInfo.ParameterType == typeof(SocketModal)) {
                            parameters.Add(arg);
                        } else if(parameterInfo.ParameterType == typeof(SocketUserCommand)) {
                            parameters.Add(arg);
                        } else if(parameterInfo.ParameterType == typeof(ApplicationDbContext)) {
                            parameters.Add(_provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>());
                        } else if(parameterInfo.ParameterType == typeof(DiscordSocketClient)) {
                            parameters.Add((DiscordSocketClient)_discord);
                        } else if(parameterInfo.ParameterType == typeof(DiscordHostedService)) {
                            parameters.Add(_discord);
                        //} else if(parameterInfo.ParameterType == typeof(APILink)) {
                        //    parameters.Add(_apilink);
                        } else if(parameterInfo.ParameterType == typeof(SocketUser)) {
                            parameters.Add(arg.User);
                        } else if(parameterInfo.ParameterType == typeof(IServiceProvider)) {
                            parameters.Add(_provider);
                        } else {
                            throw new ArgumentException($"Missing the type for {parameterInfo.Name}");
                        }
                    }

                    await (Task)command.MethodInfo.Invoke(null, parameters.ToArray());
                } catch(Exception e) {
                    try {
                        _bugsnag.Notify(e);
                        var frame = (new StackTrace(e, true)).GetFrame(0);


                        if(arg.HasResponded) {
                            await arg.ModifyOriginalResponseAsync(msg => msg.Content = $"⚠️ERROR: Bot error - {e.Message.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");

                        } else {
                            await arg.RespondAsync($"⚠️ERROR: Bot error - {e.Message.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");
                        }
                    } catch(Exception) {

                    }
                } finally {
                    _semaphoreSlim.Release();
                }

            } else {
                _bugsnag.Notify(new Exception("Command Semaphore Limit Hit"));
                await arg.RespondAsync("⚠️ERROR: Unable to run command at this time, please try again in a minute");
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
            _discord.ButtonExecuted += _discord_ButtonExecuted;
            _discord.SelectMenuExecuted += _discord_SelectMenuExecuted;
            _discord.AutocompleteExecuted += _discord_AutocompleteExecuted;
            _discord.ModalSubmitted += _discord_ModalSubmitted;

            _logger.LogInformation("Creating slash commands");
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
                    _logger.LogInformation("Creating slash commands for {guild}", guild.Name);

                    bool isCPGuild;
                    if(_cpGuild is not null)
                        isCPGuild = guild.Id == _cpGuild.Id || _cpGuild.OverflowServers.Contains(guild.Id);
                    else 
                        isCPGuild = false;

                    var discordCommands = await guild.BulkOverwriteApplicationCommandAsync((isCPGuild ? cpApplicationCommandProperties : applicationCommandProperties).ToArray());
                    _discordCommands.AddRange(discordCommands.Select(x => (x, guild.Id)));
                }
            } catch(Exception exception) {
                _bugsnag.Notify(exception);
                _logger.LogError(exception, "Error doing BulkOverwriteApplicationCommandAsync");
                var json = JsonConvert.SerializeObject(exception, Formatting.Indented);
            }

            _logger.LogInformation("Slash Commands Created");
        }

        private Task _discord_ModalSubmitted(SocketModal arg) {
            var command = _modalFunctions.First(x => x.Name == arg.Data.CustomId.ToLower());

            _ = Task.Run(() => RunCommand(command, arg));

            return Task.CompletedTask;
        }

        private Task _discord_AutocompleteExecuted(SocketAutocompleteInteraction arg) {
            _ = CallAutocompleteHandler(arg);
            return Task.CompletedTask;
        }

        private async Task CallAutocompleteHandler(SocketAutocompleteInteraction arg) {
            var timings = new TimingsFactory(_logger);
            var command = _slashCommandFunctions.First(x => x.Name == arg.Data.CommandName);
            var paremeter = command.Parameters.First(x => x.Name == arg.Data.Current.Name);

            var autocompleteHandler = (AutoCompleteHandler)DependencyInjection(paremeter.GetCustomAttributes<SlashParamAttribute>().First().AutocompleteHandler);
            timings.Set("Dependencies");
            await autocompleteHandler.Run(arg);
            timings.Finished();
        }

        private object DependencyInjection(Type T) {
            var constructor = T.GetConstructors().FirstOrDefault();
            if(constructor is null) {
                return Activator.CreateInstance(T);
            }

            var constructorParameters = constructor.GetParameters();
            var objectList = new List<object>();
            foreach(var param in constructorParameters) {
                switch(param.ParameterType.Name) {
                    case nameof(ApplicationDbContext):
                        objectList.Add(_provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>());
                        break;
                }
            }
            return Activator.CreateInstance(T, objectList.ToArray());
        }

        private Task _discord_ButtonExecuted(SocketMessageComponent arg) {
            return _discord_SelectMenuExecuted(arg);
        }

        private async Task _discord_SelectMenuExecuted(SocketMessageComponent arg) {
            try {
                var command = _componentCommandFunctions.First(x => x.Name == arg.Data.CustomId.Split(":")[0].ToLower());


                _ = Task.Run(() => RunCommand(command, arg));
            } catch(System.InvalidOperationException e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"⚠️ERROR: Bot error - Unable to locate function for the following action `{_componentCommandFunctions.First(x => x.Name == arg.Data.CustomId.Split(":")[0].ToLower())}` {arg.User.Mention}");
            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync($"⚠️ERROR: Bot error - {e.ToString()}  {frame.GetFileName()} {frame.GetFileLineNumber()} {arg.User.Mention}");
            }
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
        private Task _discord_MessageReceived(SocketMessage message) {
            _ = HandleMessageReceived(message);
            return Task.CompletedTask;
        }
        private async Task HandleMessageReceived(SocketMessage message) {
            var db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guild = message.Channel is SocketGuildChannel ? (message.Channel as SocketGuildChannel).Guild : null;
            if(((IMessage)message).Type == MessageType.UserPremiumGuildSubscription && guild.Id == _cpGuild.Id) {
                var cpGeneralChannel = guild.TextChannels.First(x => x.Id == 656455568353132546);
                //await MeritCommands.CreateMerit("Boosted the server!", db, _discord, message.Author, Guid.Empty, cpGeneralChannel);
                await cpGeneralChannel.SendMessageAsync($"{message.Author.Mention} just boosted the server!");
            }

            if(!message.Author.IsBot && message.Interaction == null) {
                var coop = await db.Coops.FirstOrDefaultAsync(x => x.DiscordChannelId == message.Channel.Id);
                if(coop is not null) {
                    var xrefs = await db.UserCoopXrefs.Include(x => x.User).Where(x => x.CoopId == coop.Id).ToListAsync();
                    //foreach(var xref in xrefs.Where(x => x.User.DiscordId != message.Author.Id)) {
                    foreach(var xref in xrefs) {
                        if(xref.CoopSetting?.PingOnMessage ?? false) {
                            var discordUser = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(xref.User.DiscordId);
                            var author = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(message.Author.Id);
                            try {
                                var dmChannel = await discordUser.CreateDMChannelAsync();
                                //await dmChannel.SendMessageAsync($"Message from <#{coop.DiscordChannelId}>, **{author.GetCleanName()}:** {message.Content}");
                            } catch(Exception e) {
                                _logger.LogError(e, "User {user} has DMs blocked", discordUser.Username);
                            }
                        }
                    }
                }
            }

            if(message.Content.StartsWith("/") && (message.Interaction is null || message.Interaction.Type != InteractionType.ApplicationCommand)) {
                var commandText = new Regex(@"^/(\w+)").Match(message.Content).Groups[1].Value.ToLower();
                var command = _slashCommandFunctions.FirstOrDefault(x => x.Name == commandText);
                if(command != null) {
                    SocketApplicationCommand discordCommand = null;
                    try {
                        discordCommand = _discordCommands.FirstOrDefault(x => x.command.Name.ToLower() == command.Name.ToLower() && x.guildid == (message.Channel as SocketGuildChannel).Guild.Id).command;
                            } finally { }
                    await message.Channel.SendMessageAsync(
                        $"⚠️{message.Author.Mention}, looks like you attempted to run the command but Discord sent it as a normal message instead of a command. Make sure a pop-up comes up when you start typing a command, if the pop-up doesn't show up then try force closing Discord and trying again. You can also try clicking on this </{command.Name}:{discordCommand?.Id}> highlighted command to run it."
                        , messageReference: new MessageReference(message.Id)
                    );
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
                AddOption(name, types.First(x => x.Key == parameterInfo.ParameterType).Value, description: slashParamDetails.Description, isRequired: slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, guildCommand, subCommand);
                return;
            }
            if(parameterInfo.ParameterType == typeof(SocketGuildUser[])) {
                for(var i = 1; i <= 10; i++) {
                    AddOption($"{name}{i}", ApplicationCommandOptionType.User, description: $"{slashParamDetails.Description} {i}", isRequired: i > 1 ? false : slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, guildCommand, subCommand);
                }
                return;
            }
            throw new NotImplementedException($"Parameter not implemented for {parameterInfo.Name} of type {parameterInfo.ParameterType}");
        }

        private void AddOption(String name, ApplicationCommandOptionType type, string description, bool isRequired, bool isAutocomplete, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null) {
            if(guildCommand != null) {
                guildCommand.AddOption(name, type, description, isRequired, isAutocomplete: isAutocomplete);
            } else {
                subCommand.AddOption(name, type, description, isRequired, isAutocomplete: isAutocomplete);
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

            _componentCommandFunctions = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(ComponentCommandAttribute), false).Length > 0)
                      .Select(x => new ComponentCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<ComponentCommandAttribute>(), Parameters = x.GetParameters() })
                      .ToList();

            _modalFunctions = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(ModalAttribute), false).Length > 0)
                      .Select(x => new ModalCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<ModalAttribute>(), Parameters = x.GetParameters() })
                      .ToList();

            CreateCommands().ConfigureAwait(false);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken) {
            _discord.SlashCommandExecuted -= _discord_SlashCommandExecuted;
            _discord.UserCommandExecuted -= _discord_UserCommandExecuted;
            _discord.MessageReceived -= _discord_MessageReceived;
            _discord.ButtonExecuted -= _discord_ButtonExecuted;
            _discord.SelectMenuExecuted -= _discord_SelectMenuExecuted;
            _discord.AutocompleteExecuted -= _discord_AutocompleteExecuted;
            _discord.ModalSubmitted += _discord_ModalSubmitted;
            _logger.LogInformation("Stopped listening to slash commands");
            if(_semaphoreSlim.CurrentCount > 0) {
                _logger.LogInformation("Waiting on semaphore to shutdown");
            }
            await _semaphoreSlim.WaitAsync(cancellationToken);
        }
    }


}
