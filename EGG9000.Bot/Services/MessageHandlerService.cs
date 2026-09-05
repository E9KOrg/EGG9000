using Discord;
using Discord.Interactions;
using Discord.WebSocket;

using EGG9000.Bot.Commands;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using EGG9000.Common.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SixLabors.ImageSharp.Formats.Png;

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Services {
    public partial class MessageHandlerService(
            DiscordHostedService discord,
            IDbContextFactory<ApplicationDbContext> dbContextFactory,
            Bugsnag.IClient bugsnag,
            ILogger<MessageHandlerService> logger,
            IConfiguration configuration,
            InteractionService interactions
        ) : IHostedService {
        private readonly DiscordHostedService _discord = discord;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory = dbContextFactory;
        private readonly Bugsnag.IClient _bugsnag = bugsnag;
        private readonly ILogger<MessageHandlerService> _logger = logger;
        private readonly InteractionService _interactions = interactions;
        private readonly Guild _cpGuild = ResolveCpGuild(configuration, dbContextFactory);

        private static Guild ResolveCpGuild(IConfiguration configuration, IDbContextFactory<ApplicationDbContext> dbContextFactory) {
            _ = ulong.TryParse(configuration.GetConnectionString("CPGuildId"), out var _CPGuildId);
            using var context = dbContextFactory.CreateDbContext();
            return context.Guilds.FirstOrDefault(x => x.Id == _CPGuildId);
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            _discord.Gateway.MessageReceived += OnMessageReceived;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) {
            _discord.Gateway.MessageReceived -= OnMessageReceived;
            return Task.CompletedTask;
        }

        private Task OnMessageReceived(SocketMessage message) {
            _ = HandleMessageReceived(message);
            return Task.CompletedTask;
        }

        private async Task HandleTestOCR(SocketMessage message, ApplicationDbContext db) {
            if(message.Attachments.Count == 0) return;
            if(message.Reference == null || message.Reference.MessageId.Value == default || message.Channel.Id != message.Reference.ChannelId) return;

            var dmChannel = await message.Author.CreateDMChannelAsync();
            var refMessage = await dmChannel.GetMessageAsync(message.Reference.MessageId.Value) as IUserMessage;

            if(refMessage.InteractionMetadata.UserId != message.Author.Id) return;

            if(!refMessage.Embeds.Any(e => e.Description.Contains("uncropped screenshot of your Privacy & Data tab"))) return;

            var attachment = message.Attachments.First();
            if(!attachment.ContentType.StartsWith("image/")) return;

            using var httpClient = new HttpClient();
            var imageStream = await httpClient.GetStreamAsync(attachment.Url);
            using var image = SixLabors.ImageSharp.Image.Load(imageStream);

            var croppedImage = EIIDScreenShots.CropScreenShot(image);
            var eiid = EIIDScreenShots.ReadText(croppedImage);

            var destThreadId = BuildConfig.IsRelease ? 1294422983904985098UL : 1294422767713652801UL;

            var destinationThread = _discord.GetChannel(destThreadId) as SocketThreadChannel;

            using var imageMs = new MemoryStream();
            image.Save(imageMs, new PngEncoder());
            var imageAttachment = new FileAttachment(imageMs, "Original_Image.png", "Original Image");

            using var croppedMs = new MemoryStream();
            croppedImage.Save(croppedMs, new PngEncoder());
            var croppedImageAttachment = new FileAttachment(croppedMs, "Cropped_Image.png", "Cropped Image");

            var embedText = $"""
                User: {message.Author.Mention}
                OCR Output: {eiid}
             """;


            var dbuser = await db.DBUsers.FirstAsync(x => x.DiscordId == message.Author.Id);

            var matchesid = dbuser.EggIncAccounts.Any(x => x.Id == eiid);
            var resultingEmbed = matchesid ? EmbedSuccess(embedText) : EmbedError(embedText);

            await destinationThread.SendFilesAsync(
                attachments: [
                    imageAttachment,
                    croppedImageAttachment,
                ],
                embed: resultingEmbed
            );

            var resultMessage = matchesid ? $"The bot was able to match the id ({eiid}) accurately to your account." : $"The bot was unable to match the id, it detected {eiid}";
            await message.Channel.SendMessageAsync(
                $"Your attempt has been processed and sent to the devs for review. {resultMessage}\n\nThank you for your assistance!",
                messageReference: new MessageReference(message.Id)
            );

            var dbUser = await db.DBUsers.Include(x => x.Merits).FirstOrDefaultAsync(u => u.DiscordId == message.Author.Id);
            if(dbUser == null) return;

            var guild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == dbUser.GuildId);

            var meritText = "Assisting the E9K devs during EID detection testing 🤖❤️";

            var hasMeritAlready = dbUser.Merits.Any(m => m.Reason == meritText);
            if(hasMeritAlready) return;

            await MeritCommands.CreateMerit(meritText, db, _discord.Gateway, message.Author, null, guild: guild);
        }

        private async Task HandleScreenshotRegistration(SocketMessage message, SocketGuild guild, ApplicationDbContext db) {
            if(message.Attachments.Count == 0) return;
            // TODO: REMOVE WHEN WE'RE READY TO GO LIVE
            if(message.Channel.Id != 1293725293403574313) return; // 1293725293403574313 = Admin testing command channel on DEV server
            if(message.Reference == null || message.Reference.MessageId.Value == default || message.Channel.Id != message.Reference.ChannelId) return;

            var welcomeChannel = BuildConfig.IsRelease
                ? await _discord.GetChannelAsync(GuildChannelType.Welcome, guild)
                : _discord.GetChannel(message.Reference.ChannelId) as SocketTextChannel;

            if(welcomeChannel == null || welcomeChannel.Id != message.Channel.Id) return;

            if((await welcomeChannel.GetMessageAsync(message.Reference.MessageId.Value)) is not IUserMessage refMessage || refMessage.Embeds.Count == 0 || !refMessage.Author.IsBot) return;

            if(refMessage.InteractionMetadata.UserId != message.Author.Id) return;

            if(!refMessage.Embeds.Any(e => e.Description.Contains("uncropped screenshot of your Privacy & Data tab"))) return;

            var attachment = message.Attachments.First();
            if(!attachment.ContentType.StartsWith("image/")) return;

            using var httpClient = new HttpClient();
            var imageStream = await httpClient.GetStreamAsync(attachment.Url);
            using var image = SixLabors.ImageSharp.Image.Load(imageStream);

            var croppedImage = EIIDScreenShots.CropScreenShot(image);
            var eiid = EIIDScreenShots.ReadText(croppedImage);
            var eiidMatch = MyRegex().Match(eiid);

            if(!eiidMatch.Success) {
                await message.Channel.SendMessageAsync(
                    "",
                    embed: EmbedError("**Unable to detect your EID from this screenshot**.\n\nPlease wait for staff assistance."),
                    messageReference: new MessageReference(message.Id)
                );
                return;
            }

            await RegisterCommandsSlash.RegisterAccountAsync(message.Channel, mut => {
                var mp = new MessageProperties();
                mut(mp);
                return message.Channel.SendMessageAsync(mp.Content.IsSpecified ? (mp.Content.Value ?? "") : "", embed: mp.Embed.IsSpecified ? mp.Embed.Value : null);
            }, db, _discord, _bugsnag, eiidMatch.Value, message.Author, _logger);
        }

        private async Task HandleMessageReceived(SocketMessage message) {
            ApplicationDbContext db = null;
            try {
                var guild = message.Channel is SocketGuildChannel ? (message.Channel as SocketGuildChannel).Guild : null;
                if(guild is not null && _cpGuild is not null && ((IMessage)message).Type == MessageType.UserPremiumGuildSubscription && guild.Id == _cpGuild.Id) {
                    _logger.LogInformation("Detected boost message in CP guild from {user}", message.Author.Username);
                    db ??= await _dbContextFactory.CreateDbContextAsync();
                    var dbGuild = await db.Guilds.FirstOrDefaultAsync(g => g.Id == guild.Id);
                    var cpGeneralChannel = guild.TextChannels.First(x => x.Id == 656455568353132546);
                    await MeritCommands.CreateMerit("Boosted the server!", db, _discord.Gateway, message.Author, Guid.Empty, guild: dbGuild);
                    await cpGeneralChannel.SendMessageAsync($"{message.Author.Mention} just boosted the server!");
                }


                if(!message.Author.IsBot && guild != null) {
                    db ??= await _dbContextFactory.CreateDbContextAsync();
                    await HandleScreenshotRegistration(message, guild, db);
                } else if(!message.Author.IsBot && message.Channel is SocketDMChannel) {
                    db ??= await _dbContextFactory.CreateDbContextAsync();
                    await HandleTestOCR(message, db);
                }

                if(!message.Author.IsBot && message.Type != MessageType.ChannelNameChange && message.Interaction == null) {
                    db ??= await _dbContextFactory.CreateDbContextAsync();
                    var coop = await db.Coops.FirstOrDefaultAsync(x => x.ThreadID == message.Channel.Id);
                    if(coop is not null) {
                        var xrefs = await db.UserCoopXrefs.Include(x => x.User).Where(x => x.CoopId == coop.Id && x.User.DiscordId != message.Author.Id).ToListAsync();
                        foreach(var xref in xrefs.Where(x => x.User.DiscordId != message.Author.Id)) {
                            if(xref.CoopSetting?.PingOnMessage ?? false) {
                                var discordUser = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(xref.User.DiscordId);
                                var author = _discord.Guilds.First(x => x.Id == coop.GuildId).GetUser(message.Author.Id);
                                if(discordUser is null) continue;
                                var dmResult = await DiscordHelpersExt.BoolSendDm(discordUser, $"Message from <#{coop.ThreadID}>, **{author.GetCleanName()}:** {message.Content}", db);
                            }
                        }
                    }
                }
            } catch(Exception ex) {
                _logger.LogError(ex, "Caught exception in HandleMessageReceived (top-level)");
                _bugsnag.Notify(ex);
            } finally {
                if(db is not null) await db.DisposeAsync();
            }

            if(message.Content.StartsWith('/') && (message.Interaction is null || message.Interaction.Type != InteractionType.ApplicationCommand)) {
                var commandTextMatches = CommandRegex().Match(message.Content);
                if(commandTextMatches.Success) {
                    var topLevelName = "";
                    try {
                        topLevelName = commandTextMatches.Groups[1].Value.ToLower().Trim();
                    } catch(Exception ex) { _logger.LogError("Caught exception in HandleMessageReceived (INT-1):\n {exception}", ex); return; }

                    if(message.Channel is not SocketGuildChannel guildChannel) return;
                    var socketGuild = guildChannel.Guild;

                    SocketApplicationCommand discordCommand;
                    try {
                        discordCommand = (await socketGuild.GetCachedApplicationCommands())
                            .FirstOrDefault(x => x.Type == ApplicationCommandType.Slash && x.Name.Equals(topLevelName, StringComparison.CurrentCultureIgnoreCase));
                        discordCommand ??= (await _discord.GetCachedApplicationCommands())
                            .FirstOrDefault(x => x.Type == ApplicationCommandType.Slash && x.Name.Equals(topLevelName, StringComparison.CurrentCultureIgnoreCase));
                    } catch(Exception ex) { _logger.LogError("Caught exception in HandleMessageReceived (INT-2):\n {exception}", ex); return; }

                    if(discordCommand != null) {
                        var permChannel = message.Channel switch {
                            SocketThreadChannel threadChannel => threadChannel.ParentChannel,
                            SocketTextChannel textChannel => textChannel,
                            _ => null
                        };
                        var canUseCommandsInChannel = !permChannel?.PermissionOverwrites?.Any(p => p.Permissions.UseApplicationCommands == PermValue.Deny) ?? true;

                        if(canUseCommandsInChannel) {
                            var commandLink = await _discord.GetSlashCommandStringAsync(socketGuild, topLevelName);
                            var warningEmbed = EmbedWarning(
                                $"Looks like you attempted to run a command but Discord sent it as a normal message instead. Make sure a pop-up comes up when you start typing a command, " +
                                $"if the pop-up doesn't show up then try force closing Discord and trying again. You can also click on " +
                                $"{commandLink} to run it."
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

        [GeneratedRegex(@"^\/(\w+)(?:\s+(\w+))?")]
        private static partial Regex CommandRegex();
        [GeneratedRegex(@"EI\d{16}")]
        private static partial Regex MyRegex();
    }
}
