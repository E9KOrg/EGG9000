
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using EGG9000.Bot.Automated;
using EGG9000.Bot.Automated.Coops;
using EGG9000.Bot.Commands;
using EGG9000.Bot.Helpers;
using EGG9000.Common.Commands;
using EGG9000.Common.Consumers;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Extensions;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static EGG9000.Common.Helpers.Discord.EmbedHelpers;
using static EGG9000.Common.Services.FauxCommand;

namespace EGG9000.Bot.Services {

    public class UserNotInServerException(string message, ulong user) : Exception(message) {
        public ulong User { get; } = user;
    }

    public class CommandService : IHostedService {
        private readonly DiscordHostedService _discord;
        private List<SlashCommandFunction> _slashCommandFunctions;
        private List<UserCommandFunction> _userCommandFunctions;
        private List<ComponentCommandFunction> _componentCommandFunctions;
        private List<ModalCommandFunction> _modalFunctions;
        private readonly APILink _apilink;
        private readonly Words _words;
        private readonly Bugsnag.IClient _bugsnag;
        private readonly SemaphoreSlim _semaphoreSlim = new(50);
        private readonly ContractUpdater _contractUpdater;
        //private readonly CoopStatusUpdater _coopStatusUpdater;
        private readonly ThreadsCoopStatusUpdater _coopStatusUpdaterThreads;
        private readonly JobService _jobService;
        private readonly Guild _cpGuild;
        private readonly IServiceProvider _provider;
        private readonly ILogger<CommandService> _logger;
        private readonly List<(SocketApplicationCommand command, ulong guildid)> _discordCommands = [];
        private readonly List<(SocketApplicationCommand command, ulong guildid)> _globalCommands = [];
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IMemoryCache _cache;
        public CommandService(IConfiguration Configuration,
                DiscordHostedService discord,
                APILink apilink,
                Words words,
                Bugsnag.IClient bugsnag,
                ContractUpdater contractUpdater,
                //CoopStatusUpdater coopStatusUpdater,
                ThreadsCoopStatusUpdater coopStatusUpdaterThreads,
                JobService jobService,
                ApplicationDbContext context,
                IServiceProvider serviceProvider,
                ILogger<CommandService> logger,
                IPublishEndpoint publishEndpoint, IDbContextFactory<ApplicationDbContext> dbContextFactory,
                IMemoryCache cache
            ) {
            _discord = discord;
            _apilink = apilink;
            _words = words;
            _publishEndpoint = publishEndpoint;

            _bugsnag = bugsnag;
            _contractUpdater = contractUpdater;
            //_coopStatusUpdater = coopStatusUpdater;
            _coopStatusUpdaterThreads = coopStatusUpdaterThreads;
            _jobService = jobService;
            _ = ulong.TryParse(Configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);
            _cpGuild = context.Guilds.FirstOrDefault(x => x.Id == _CPGuildId);
            _provider = serviceProvider;
            _logger = logger;
            _dbContextFactory = dbContextFactory;
            _cache = cache;
            logger.LogInformation($"Initiating CommandService");
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
                await arg.RespondAsync(text: "", embed: EmbedExceptionFrame(e));
            }
        }

        private async Task _discord_UserCommandExecuted(SocketUserCommand arg) {
            try {
                var command = _userCommandFunctions.First(x => x.Name == arg.Data.Name || x.Details.Name == arg.Data.Name);
                if(command == null) return;
                _ = Task.Run(() => RunCommand(command, arg));
            } catch(Exception e) {
                _bugsnag.Notify(e);
                await arg.RespondAsync(text: "", embed: EmbedExceptionFrame(e));
            }
        }


        private async Task RunCommand(CommandFunctionBase command, IDiscordInteraction arg) {
            //_ = arg.DeferAsync();
            if(await _semaphoreSlim.WaitAsync(TimeSpan.FromSeconds(2.5))) {
                try {
                    var parameters = new List<object>();
                    foreach(var parameterInfo in command.Parameters) {
                        if(parameterInfo.GetCustomAttributes<SlashParamAttribute>().Any()) {
                            parameters.Add(GetParam(parameterInfo, arg));
                            continue;
                        }
                        if(parameterInfo.GetCustomAttributes<ComponentDataAttribute>().Any()) {
                            string[] data;
                            if(arg is SocketModal modal) {
                                data = modal.Data.CustomId.Split(":");
                            } else {
                                data = ((SocketMessageComponent)arg).Data.CustomId.Split(":");
                            }
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
                            parameters.Add(await _dbContextFactory.CreateDbContextAsync());
                        } else if(parameterInfo.ParameterType == typeof(DiscordSocketClient)) {
                            parameters.Add(_discord);
                        } else if(parameterInfo.ParameterType == typeof(DiscordHostedService)) {
                            parameters.Add(_discord);
                        } else if(parameterInfo.ParameterType == typeof(APILink)) {
                            parameters.Add(_apilink);
                        } else if(parameterInfo.ParameterType == typeof(Words)) {
                            parameters.Add(_words);
                        } else if(parameterInfo.ParameterType == typeof(SocketUser)) {
                            parameters.Add(arg.User);
                        //} else if(parameterInfo.ParameterType == typeof(CoopStatusUpdater)) {
                            //parameters.Add(_coopStatusUpdater);
                        } else if(parameterInfo.ParameterType == typeof(ThreadsCoopStatusUpdater)) {
                            parameters.Add(_coopStatusUpdaterThreads);
                        } else if(parameterInfo.ParameterType == typeof(ContractUpdater)) {
                            parameters.Add(_contractUpdater);
                        } else if(parameterInfo.ParameterType == typeof(JobService)) {
                            parameters.Add(_jobService);
                        } else if(parameterInfo.ParameterType == typeof(IServiceProvider)) {
                            parameters.Add(_provider);
                        } else if(parameterInfo.ParameterType == typeof(Bugsnag.IClient)) {
                            parameters.Add(_bugsnag);
                        } else if(parameterInfo.ParameterType == typeof(ILogger)) {
                            parameters.Add(_logger);
                        }else if(parameterInfo.ParameterType == typeof(IMemoryCache)) {
                            parameters.Add(_cache);
                        } else {
                            throw new ArgumentException($"Parameter `{parameterInfo.Name}` is of type `{parameterInfo.ParameterType}`, which has not been implemented to be passed to commands.");
                        }
                    }

                    _logger.LogInformation("Running command {command} for user: {username}", command.Name, arg.User.Username);
                    await (Task)command.MethodInfo.Invoke(null, [..parameters]);
                } catch(UserNotInServerException unfe) {
                    await arg.RespondAsync(text: "", embed: EmbedError($"Could not convert the id `{unfe.User}` to a `SocketGlobalUser` instance.\nUser (<@{unfe.User}>) may not be in the server anymore."));
                } catch(InvalidOperationException) {
                    await arg.RespondAsync(text: "", embed: EmbedError("One or more parameters for your command were passed as plain-text instead of selectable options, and could not be parsed"));
                } catch(Exception e) {
                    try {
                        _bugsnag.Notify(e);
                        if(arg.HasResponded) {
                            await arg.ModifyOriginalResponseAsync(msg => { msg.Content = ""; msg.Embed = EmbedExceptionFrame(e); });
                        } else {
                            await arg.RespondAsync(text: "", embed: EmbedExceptionFrame(e));
                        }
                    } catch(Exception) {

                    }
                } finally {
                    _semaphoreSlim.Release();
                }

            } else {
                _bugsnag.Notify(new Exception("Command Semaphore Limit Hit"));
                _logger.LogWarning("Command Semaphore Limit Hit");
                await arg.RespondAsync(text: "", embed: EmbedError("Unable to run command at this time, please try again in a minute"));
            }
        }



        private static FauxSocketSlashCommandDataOption FindOption(string name, IList<FauxSocketSlashCommandDataOption> options) {
            var foundOption = options.FirstOrDefault(x => x.Name == name);
            if(foundOption != null) {
                return foundOption;
            }

            foreach(var option in options) {
                if(option.Options != null) {
                    foundOption = FindOption(name, [..option.Options]);
                    if(foundOption != null) {
                        return foundOption;
                    }
                }
            }
            return null;
        }

        private async Task CreateCommands() {
            try {

                _discord.SlashCommandExecuted += _discord_SlashCommandExecuted;
                _discord.UserCommandExecuted += _discord_UserCommandExecuted;
                _discord.MessageReceived += _discord_MessageReceived;
                _discord.ButtonExecuted += _discord_ButtonExecuted;
                _discord.SelectMenuExecuted += _discord_SelectMenuExecuted;
                _discord.AutocompleteExecuted += _discord_AutocompleteExecuted;
                _discord.ModalSubmitted += _discord_ModalSubmitted;

                await _publishEndpoint.Publish(new ShutdownMessage());


                _logger.LogInformation("Creating slash commands");
                List<ApplicationCommandProperties> guildCommandProperties = [];
                List<ApplicationCommandProperties> globalCommandProperties = [];

                foreach(var command in _slashCommandFunctions) {
                    var guildCommand = new SlashCommandBuilder {
                        Name = command.Name,
                        Description = $"{(command.Details.AdminOnly != StaffOnlyLevel.None ? "(Admin Only)":"")} {command.Details.Description}",
                        DefaultMemberPermissions = command.Details.AdminOnly switch {
                            StaffOnlyLevel.Admin => (GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles),
                            StaffOnlyLevel.CluckingCoordinator => GuildPermission.ManageChannels,
                            StaffOnlyLevel.FarmHand => GuildPermission.CreatePrivateThreads,
                            StaffOnlyLevel.ChickenTender => GuildPermission.ModerateMembers,
                            _ => GuildPermission.UseApplicationCommands
                        }
                    };

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
                    if(command.Details.AllowInDMs || (command.SubFunctions?.All(x => x.Details.AllowInDMs) ?? false)) {
                        globalCommandProperties.Add(guildCommand.Build());
                    } else {
                        guildCommandProperties.Add(guildCommand.Build());
                    }
                }

                foreach(var command in _userCommandFunctions) {
                    var guildCommand = new UserCommandBuilder {
                        DefaultMemberPermissions = GuildPermission.UseApplicationCommands
                    };
                    guildCommand.WithName(command.Details.Name ?? command.Name);
                    command.Name = command.Details.Name ?? command.Name;

                    if(command.Details.AdminOnly != StaffOnlyLevel.None) {
                        guildCommand.DefaultMemberPermissions = command.Details.AdminOnly switch {
                            StaffOnlyLevel.Admin => (GuildPermission.Administrator | GuildPermission.ManageChannels | GuildPermission.ManageRoles),
                            StaffOnlyLevel.CluckingCoordinator => GuildPermission.ManageChannels,
                            StaffOnlyLevel.FarmHand => GuildPermission.CreatePrivateThreads,
                            StaffOnlyLevel.ChickenTender => GuildPermission.ModerateMembers,
                            _ => GuildPermission.UseApplicationCommands
                        };
                    }

                    guildCommandProperties.Add(guildCommand.Build());
                }

                var globalCommands = await _discord.BulkOverwriteGlobalApplicationCommandsAsync([..globalCommandProperties]);
                _globalCommands.AddRange(globalCommands.Select(y => (y, (ulong)0)));
                foreach(var guild in _discord.Guilds) {
                    _logger.LogInformation("Creating slash commands for {guild}", guild.Name);

                    var discordCommands = await guild.BulkOverwriteApplicationCommandAsync([..guildCommandProperties]);
                    _discordCommands.AddRange(discordCommands.Select(x => (x, guild.Id)));
                }
            } catch(Exception exception) {
                _bugsnag.Notify(exception);

                _logger.LogError(exception, "Error creating slash commands");
            }

            _logger.LogInformation("Slash Commands Created");
        }


        private Task _discord_ModalSubmitted(SocketModal arg) {
            var command = _modalFunctions.First(x => x.Name == arg.Data.CustomId.ToLower().Split(":")[0]);

            _ = Task.Run(() => RunCommand(command, arg));

            return Task.CompletedTask;
        }

        private Task _discord_AutocompleteExecuted(SocketAutocompleteInteraction arg) {
            var command = _slashCommandFunctions.First(x => x.Name == arg.Data.CommandName);

            if(command.SubFunctions != null) {
                command = command.SubFunctions.First(x => x.Name == arg.Data.Options.First().Name);
            }

            var paremeter = command.Parameters.First(x => x.Name.Equals(arg.Data.Current.Name, StringComparison.OrdinalIgnoreCase));

            var handler = paremeter.GetCustomAttributes<SlashParamAttribute>().First().AutocompleteHandler;

            var autocompleteClass = ActivatorUtilities.CreateInstance(_provider, handler);
            handler.GetMethod("Run").Invoke(autocompleteClass, [arg]);
            return Task.CompletedTask;
        }


        private Task _discord_ButtonExecuted(SocketMessageComponent arg) {
            return _discord_SelectMenuExecuted(arg);
        }

        private async Task _discord_SelectMenuExecuted(SocketMessageComponent arg) {
            try {
                var command = _componentCommandFunctions.First(x => x.Name == arg.Data.CustomId.Split(":")[0].ToLower());


                _ = Task.Run(() => RunCommand(command, arg));

            } catch(Exception e) {
                _bugsnag.Notify(e);
                var frame = (new StackTrace(e, true)).GetFrame(0);

                await arg.RespondAsync(text: "", embed: EmbedExceptionFrame(e));
            }
        }



        private static object GetParam(ParameterInfo parameterInfo, IDiscordInteraction arg) {
            if(arg is FauxCommand) {
                return null;
            } else {
                var fauxCommand = arg is SocketSlashCommand ? new FauxCommand(arg as SocketSlashCommand) : arg as FauxCommand;
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
                if(parameterInfo.ParameterType == typeof(SocketUser[])) {
                    var users = new List<SocketUser>();
                    for(var i = 1; i <= 10; i++) {
                        var option = FindOption($"{name}{i}", fauxCommand.Data.Options);
                        if(option != null) {
                            users.Add((SocketUser)option.Value);
                        }
                    }
                    return users.ToArray();
                }
                if(parameterInfo.ParameterType == typeof(SocketGuildUser)) {
                    var value = FindOption(name, fauxCommand.Data.Options)?.Value;
                    try {
                        var user = (SocketGuildUser)value;
                        return user;
                    } catch(InvalidCastException ex) {
                        throw new UserNotInServerException(ex.Message, (value as SocketUser).Id);
                    }
                }
                if(parameterInfo.ParameterType.IsEnum) {
                    var value = FindOption(name, fauxCommand.Data.Options)?.Value;
                    return value == null ? null : Enum.Parse(parameterInfo.ParameterType, value.ToString());
                }
                var optionResult = FindOption(name, fauxCommand.Data.Options);
                if(optionResult == null) {
                    return null;
                }

                if(parameterInfo.ParameterType == typeof(uint)) {
                    return Convert.ToUInt32(FindOption(name, fauxCommand.Data.Options)?.Value);
                } else if(parameterInfo.ParameterType == typeof(int)) {
                    return Convert.ToInt32(FindOption(name, fauxCommand.Data.Options)?.Value);
                } else {
                    return FindOption(name, fauxCommand.Data.Options)?.Value;
                }
            }
        }
        private Task _discord_MessageReceived(SocketMessage message) {
            _ = HandleMessageReceived(message);
            return Task.CompletedTask;
        }
        
        private async Task HandleTestOCR(SocketMessage message, ApplicationDbContext db) {
            if(message.Attachments.Count == 0) return;
            if(message.Reference == null || message.Reference.MessageId.Value == default || message.Channel.Id != message.Reference.ChannelId) return;

            var dmChannel = await message.Author.CreateDMChannelAsync();
            var refMessage = await dmChannel.GetMessageAsync(message.Reference.MessageId.Value) as IUserMessage;

            // Make sure the users match
            if(refMessage.InteractionMetadata.UserId != message.Author.Id) return;

            // Make sure the user was prompted by the bot to send a screenshot
            if(!refMessage.Embeds.Any(e => e.Description.Contains("uncropped screenshot of your Privacy & Data tab"))) return;

            // Make sure the attachment is an image (check its ContentType)
            var attachment = message.Attachments.First();
            if(!attachment.ContentType.StartsWith("image/")) return;

            using var httpClient = new HttpClient();
            // Download the image from the attachment's URL
            var imageStream = await httpClient.GetStreamAsync(attachment.Url);
            using var image = SixLabors.ImageSharp.Image.Load(imageStream);
            var rgbaImage = image.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>();

            // Crop the image
            var croppedImage = TesseractHelper.GetCroppedImage(rgbaImage);

            // Run tesseract - will either return an EI matching regex, or an empty string.
            var (eidMatch, extractedText) = TesseractHelper.RunTesseract(croppedImage);

#if RELEASE
            ulong destThreadId = 1294422983904985098;
#else
            ulong destThreadId = 1294422767713652801;
#endif

            var destinationThread = _discord.GetChannel(destThreadId) as SocketThreadChannel;

            // Save the images back to streams
            using var imageMs = new MemoryStream();
            image.Save(imageMs, new PngEncoder());
            var imageAttachment = new FileAttachment(imageMs, "Original_Image.png", "Original Image");

            using var croppedMs = new MemoryStream();
            croppedImage.Save(croppedMs, new PngEncoder());
            var croppedImageAttachment = new FileAttachment(croppedMs, "Cropped_Image.png", "Cropped Image");

            var embedText = $"""
                User: {message.Author.Mention}
                OCR Output: {(string.IsNullOrEmpty(eidMatch.Value) ? extractedText : eidMatch.Value)}
             """;

            var resultingEmbed = eidMatch.Success ? EmbedSuccess(embedText) : EmbedError(embedText);

            await destinationThread.SendFilesAsync(
                attachments: [
                    imageAttachment,
                    croppedImageAttachment,
                ],
                embed: resultingEmbed
            );

            await message.Channel.SendMessageAsync(
                "Your attempt has been processed and sent to the devs for review.\n\nThank you for your assistance!",
                messageReference: new MessageReference(message.Id)
            );

            var dbUser = await db.DBUsers.FirstOrDefaultAsync(u => u.DiscordId == message.Author.Id);
            if(dbUser == null) return;

            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == dbUser.GuildId);

            var meritText = "Assisting the E9K devs during EID detection testing 🤖❤️";

            var hasMeritAlready = dbUser.Merits.Any(m => m.Reason == meritText);
            if(hasMeritAlready) return;

            await MeritCommands.CreateMerit(meritText, db, _discord, message.Author, Guid.Empty, guild);
        }

        private async Task HandleScreenshotRegistration(SocketMessage message, SocketGuild guild, ApplicationDbContext db) {
            if(message.Attachments.Count == 0) return;
            // TODO: REMOVE WHEN WE'RE READY TO GO LIVE
            if(message.Channel.Id != 1293725293403574313) return; // 1293725293403574313 = Admin testing command channel on DEV server
            if(message.Reference == null || message.Reference.MessageId.Value == default || message.Channel.Id != message.Reference.ChannelId) return;

#if RELEASE
            var welcomeChannel = await _discord.GetChannelAsync(GuildChannelType.Welcome, guild);
#else
            var welcomeChannel = _discord.GetChannel(message.Reference.ChannelId) as SocketTextChannel;
#endif

            if(welcomeChannel == null || welcomeChannel.Id != message.Channel.Id) return;

            // Lookup the message that this was in reply to
            if((await welcomeChannel.GetMessageAsync(message.Reference.MessageId.Value)) is not IUserMessage refMessage || refMessage.Embeds.Count == 0 || !refMessage.Author.IsBot) return;

            // Make sure the users match
            if(refMessage.InteractionMetadata.UserId != message.Author.Id) return;

            // Make sure the user was prompted by the bot to send a screenshot
            if(!refMessage.Embeds.Any(e => e.Description.Contains("uncropped screenshot of your Privacy & Data tab"))) return;

            // Make sure the attachment is an image (check its ContentType)
            var attachment = message.Attachments.First();
            if(!attachment.ContentType.StartsWith("image/")) return;

            using var httpClient = new HttpClient();
            // Download the image from the attachment's URL
            var imageStream = await httpClient.GetStreamAsync(attachment.Url);
            using var image = SixLabors.ImageSharp.Image.Load(imageStream);
            var rgbaImage = image.CloneAs<SixLabors.ImageSharp.PixelFormats.Rgba32>();

            // Crop the image
            var croppedImage = TesseractHelper.GetCroppedImage(rgbaImage);

            // Run tesseract - will either return an EI matching regex, or an empty string.
            var (eidMatch, extractedText) = TesseractHelper.RunTesseract(croppedImage);

            if (!eidMatch.Success) {
                await message.Channel.SendMessageAsync(
                    "",
                    embed: EmbedError("**Unable to detect your EI number from this screenshot**.\n\nPlease wait for staff assistance."),
                    messageReference: new MessageReference(message.Id)
                );
                return;
            }

            // Construct a new FauxCommand so we can hook into Registering
            var command = new FauxCommand(message, guild.Id);

            await RegisterCommandsSlash._Register(command, db, _discord, _apilink, _bugsnag, eidMatch.Value, message.Author, _logger);
        }

        private async Task HandleMessageReceived(SocketMessage message) {
            var db = await _dbContextFactory.CreateDbContextAsync();
            var guild = message.Channel is SocketGuildChannel ? (message.Channel as SocketGuildChannel).Guild : null;
            if(((IMessage)message).Type == MessageType.UserPremiumGuildSubscription && guild.Id == _cpGuild.Id) {
                var cpGeneralChannel = guild.TextChannels.First(x => x.Id == 656455568353132546);
                await MeritCommands.CreateMerit("Boosted the server!", db, _discord, message.Author, Guid.Empty);
                await cpGeneralChannel.SendMessageAsync($"{message.Author.Mention} just boosted the server!");
            }

            
            if (!message.Author.IsBot && guild != null) {
                await HandleScreenshotRegistration(message, guild, db);
            } else if (!message.Author.IsBot && message.Channel is SocketDMChannel) {
                await HandleTestOCR(message, db);
            }

            if(!message.Author.IsBot && message.Type != MessageType.ChannelNameChange && message.Interaction == null) {
                var coop = await db.Coops.FirstOrDefaultAsync(x => x.ThreadID == message.Channel.Id || x.DiscordChannelId == message.Channel.Id);
                if(coop is not null) {
                    var xrefs = await db.UserCoopXrefs.Include(x => x.User).Where(x => x.CoopId == coop.Id && x.User.DiscordId != message.Author.Id).ToListAsync();
                    foreach(var xref in xrefs.Where(x => x.User.DiscordId != message.Author.Id)) {
                        if(xref.CoopSetting?.PingOnMessage ?? false) {
                            var discordUser = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(xref.User.DiscordId);
                            var author = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(message.Author.Id);
                            if(discordUser is null) continue; //Another null check
                            var dmResult = await DiscordHelpersExt.BoolSendDm(discordUser, $"Message from <#{(coop.ThreadID != 0 ? coop.ThreadID : coop.DiscordChannelId)}>, **{author.GetCleanName()}:** {message.Content}", db);
                        }
                    }
                }
            }

            if(message.Content.StartsWith("/") && (message.Interaction is null || message.Interaction.Type != InteractionType.ApplicationCommand)) {
                var commandTextMatches = new Regex(@"^\/(\w+)(?:\s+(\w+))?").Match(message.Content);
                if(commandTextMatches.Success) {
                    var foundParentCommand = "";
                    var foundCommandText = "";
                    try {
                        if(commandTextMatches.Groups[2].Success) {
                            foundParentCommand = commandTextMatches.Groups[1].Value.ToLower().Trim();
                            foundCommandText = commandTextMatches.Groups[2].Value.ToLower().Trim();
                        } else foundCommandText = commandTextMatches.Groups[1].Value.ToLower().Trim();
                    } catch(Exception ex) { _logger.LogError("Caught exception in HandleMessageReceived (INT-1):\n {exception}", ex); return; }

                    var global = false;
                    SocketApplicationCommand discordCommand = null;
                    try {
                        if(foundParentCommand == "") discordCommand = _discordCommands.First(x => x.command.Type == ApplicationCommandType.Slash && x.command.Name.ToLower() == foundCommandText && (x.guildid == (message.Channel as SocketGuildChannel).Guild.Id) || x.guildid == 0).command;
                        else discordCommand = _discordCommands.First(x => x.command.Type == ApplicationCommandType.Slash && x.command.Name.ToLower() == foundParentCommand && (x.guildid == (message.Channel as SocketGuildChannel).Guild.Id) || x.guildid == 0).command;
                    } catch(Exception ex) { _logger.LogError("Caught exception in HandleMessageReceived (INT-2):\n {exception}", ex); return; }

                    if(discordCommand == null) {
                        try {
                            if(foundParentCommand == "") discordCommand = _globalCommands.First(x => x.command.Type == ApplicationCommandType.Slash && x.command.Name.ToLower() == foundCommandText).command;
                            else discordCommand = _globalCommands.First(x => x.command.Type == ApplicationCommandType.Slash && x.command.Name.ToLower() == foundParentCommand).command;
                            if(discordCommand != null) global = true;
                        } catch(Exception ex) { _logger.LogError("Caught exception in HandleMessageReceived (INT-3):\n {exception}", ex); return; }
                    }

                    if(discordCommand != null) {
                        var commands = _slashCommandFunctions.Where(s => s.Name == (foundParentCommand == "" ? foundCommandText : foundParentCommand) && !_userCommandFunctions.Any(u => u.Equals(s)));
                        SlashCommandFunction command = null;
                        if(foundParentCommand == "") command = commands.First(s => s.SubFunctions == null || s.SubFunctions.Count == 0);
                        else command = commands.First(s => s.SubFunctions?.Count > 0);
                        var parentHasChild = false;
                        var bypass = false;
                        if(command.SubFunctions is null || command.SubFunctions.Count == 0) bypass = true;
                        else parentHasChild = command.SubFunctions.Any(s => s.Name == foundCommandText);
                        var hasPerms = false;
                        if(global) hasPerms = true;
                        else {
                            var adminOnlyLevel = command == null ? StaffOnlyLevel.None : command.Details.AdminOnly;
                            var associatedPerm = adminOnlyLevel switch {
                                StaffOnlyLevel.Admin => GuildPermission.Administrator,
                                StaffOnlyLevel.CluckingCoordinator => GuildPermission.ManageChannels,
                                StaffOnlyLevel.FarmHand => GuildPermission.CreatePrivateThreads,
                                StaffOnlyLevel.ChickenTender => GuildPermission.ModerateMembers,
                                _ => GuildPermission.UseApplicationCommands
                            };
                            hasPerms = command.Details.AdminOnly == StaffOnlyLevel.None || (message.Author as SocketGuildUser).GuildPermissions.ToList().Contains(associatedPerm);
                        }

                        var canUseCommandsInChannel = !(message.Channel as SocketGuildChannel)?.PermissionOverwrites?.Any(p => p.Permissions.UseApplicationCommands == PermValue.Deny) ?? true;
                        if(hasPerms && (parentHasChild || bypass) && canUseCommandsInChannel) {
                            var warningEmbed = EmbedWarning(
                                $"Looks like you attempted to run a command but Discord sent it as a normal message instead. Make sure a pop-up comes up when you start typing a command, " +
                                $"if the pop-up doesn't show up then try force closing Discord and trying again. You can also click on " +
                                $"</{(foundParentCommand != "" ? $"{foundParentCommand}" + (parentHasChild ? " " : "") : "")}" +
                                $"{(parentHasChild ? commandTextMatches.Groups[2] : foundCommandText)}:{discordCommand?.Id}> to run it."
                            );

                            await message.Channel.SendMessageAsync(
                                embed: warningEmbed,
                                messageReference: new MessageReference(message.Id)
                            );
                        }
                    }
                }
            }
        }


        private static void AddCommandParams(ParameterInfo parameterInfo, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null) {
            var slashParamDetails = parameterInfo.GetCustomAttribute<SlashParamAttribute>();
            var name = parameterInfo.Name.ToLower();
            if(string.IsNullOrEmpty(slashParamDetails.Description)) {
                slashParamDetails.Description = name;
            }

            var types = new Dictionary<Type, ApplicationCommandOptionType> {
                {typeof(int), ApplicationCommandOptionType.Integer },
                {typeof(ulong), ApplicationCommandOptionType.Integer },
                {typeof(uint), ApplicationCommandOptionType.Integer },
                {typeof(string), ApplicationCommandOptionType.String },
                {typeof(bool), ApplicationCommandOptionType.Boolean },
                {typeof(SocketGuildUser), ApplicationCommandOptionType.User },
                {typeof(SocketUser), ApplicationCommandOptionType.User },
                {typeof(SocketChannel), ApplicationCommandOptionType.Channel },
                {typeof(SocketRole), ApplicationCommandOptionType.Role },
            };

            if(types.Any(x => x.Key == parameterInfo.ParameterType)) {
                var maxVal = parameterInfo.ParameterType == typeof(int) ? int.MaxValue : double.MinValue;
                AddOption(name, types.First(x => x.Key == parameterInfo.ParameterType).Value, description: slashParamDetails.Description, isRequired: slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, positiveOnly: slashParamDetails.PositiveOnly, maxValue: maxVal, 0, slashParamDetails.StringMaxLength, guildCommand, subCommand);
                return;
            }
            if(parameterInfo.ParameterType.IsEnum) {
                List<ApplicationCommandOptionChoiceProperties> choices = [];
                foreach(var value in Enum.GetValues(parameterInfo.ParameterType)) {
                    choices.Add(new ApplicationCommandOptionChoiceProperties {
                        Name = Enums.GetAttribute<Discord.Interactions.ChoiceDisplayAttribute>(value)?.Name ?? value.ToString(),
                        Value = Convert.ToInt32(value)
                    });
                }
                AddOption(name, ApplicationCommandOptionType.Integer, description: slashParamDetails.Description, isRequired: slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, positiveOnly: slashParamDetails.PositiveOnly, maxValue: double.MinValue, 0, int.MaxValue, guildCommand, subCommand, [..choices]);
                return;
            }
            if(parameterInfo.ParameterType == typeof(SocketUser[])) {
                for(var i = 1; i <= 10; i++) {
                    AddOption($"{name}{i}", ApplicationCommandOptionType.User, description: $"{slashParamDetails.Description} {i}", isRequired: i <= 1 && slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, positiveOnly: slashParamDetails.PositiveOnly, maxValue: double.MinValue, 0, int.MaxValue, guildCommand, subCommand);
                }
                return;
            }
            if(parameterInfo.ParameterType == typeof(SocketGuildUser[])) {
                for(var i = 1; i <= 10; i++) {
                    AddOption($"{name}{i}", ApplicationCommandOptionType.User, description: $"{slashParamDetails.Description} {i}", isRequired: i <= 1 && slashParamDetails.Required, isAutocomplete: slashParamDetails.AutocompleteHandler is not null, positiveOnly: slashParamDetails.PositiveOnly, maxValue: double.MinValue, 0, int.MaxValue, guildCommand, subCommand);
                }
                return;
            }
            throw new NotImplementedException($"Parameter not implemented for {parameterInfo.Name} of type {parameterInfo.ParameterType}");
        }

        private static void AddOption(
            string name, ApplicationCommandOptionType type, string description, bool isRequired, bool isAutocomplete, bool positiveOnly, double maxValue, int minLength, int maxLength, SlashCommandBuilder guildCommand = null, SlashCommandOptionBuilder subCommand = null, params ApplicationCommandOptionChoiceProperties[] choices
        ) {
            if(guildCommand != null) {
                guildCommand.AddOption(
                    name: name,
                    type: type,
                    description: description,
                    isRequired: isRequired,
                    isDefault: null,
                    isAutocomplete: isAutocomplete,
                    minValue: positiveOnly ? 0 : null,
                    maxValue: maxValue == double.MinValue ? null : maxValue,
                    options: null,
                    channelTypes: null,
                    nameLocalizations: null,
                    descriptionLocalizations: null,
                    minLength: minLength,
                    maxLength: maxLength,
                    choices: choices
                );
            } else {
                subCommand.AddOption(
                    name: name,
                    type: type,
                    description: description,
                    isRequired: isRequired,
                    isDefault: false,
                    isAutocomplete: isAutocomplete,
                    minValue: positiveOnly ? 0 : null,
                    maxValue: maxValue == double.MinValue ? null : maxValue,
                    options: null,
                    channelTypes: null,
                    nameLocalizations: null,
                    descriptionLocalizations: null,
                    minLength: minLength,
                    maxLength: maxLength,
                    choices: choices
                );
            }

        }

        public Task StartAsync(CancellationToken cancellationToken) {
            var slashCommands = AppDomain.CurrentDomain.GetAssemblies().SelectMany(x => x.GetTypes())
                      .SelectMany(t => t.GetMethods())
                      .Where(m => m.GetCustomAttributes(typeof(SlashCommandAttribute), false).Length > 0)
                      .Select(x => new SlashCommandFunction { Name = x.Name.ToLower(), MethodInfo = x, Details = x.GetCustomAttribute<SlashCommandAttribute>(), Parameters = x.GetParameters() })
                      .ToList();



            _slashCommandFunctions = [
                .. slashCommands.Where(x => string.IsNullOrWhiteSpace(x.Details.ParentCommand)),
                .. slashCommands
                    .Where(x => !string.IsNullOrWhiteSpace(x.Details.ParentCommand))
                    .GroupBy(x => x.Details.ParentCommand)
                    .Select(x => new SlashCommandFunction {
                        Name = x.Key,
                        SubFunctions = [..x],
                        Details = new SlashCommandAttribute { Description = "?", AdminOnly = x.Min(y => y.Details.AdminOnly) },
                    })
            ];

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
