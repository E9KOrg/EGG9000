// Ignore Spelling: Faux

using Discord;
using Discord.WebSocket;
using EGG9000.Common.Helpers.Discord;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public partial class FauxCommand : IDiscordInteraction {
        private readonly SocketMessage _originalMessage;
        private readonly SocketSlashCommand _socketSlashCommand;
        private readonly SocketUserCommand _socketUserCommand;
        private readonly SocketCommandBase _socketCommandBase;

        private IUserMessage _message;
        private readonly bool _fake = false;
        private readonly ulong? _guildid;

        public FauxCommand(SocketMessage message, ulong? guildid) {
            _originalMessage = message;
            _guildid = guildid;
        }
        public FauxCommand(SocketSlashCommand command) {
            _socketSlashCommand = command;
            _socketCommandBase = command;
        }

        public FauxCommand(SocketUserCommand command) {
            _socketUserCommand = command;
            _socketCommandBase = command;
        }

        public FauxCommand(bool fake = true) {
            _fake = fake;
        }

        public static FauxCommand CreateFake() => new(fake: true);

        public static implicit operator FauxCommand(SocketSlashCommand command) {
            return new FauxCommand(command);
        }

        public static implicit operator FauxCommand(SocketUserCommand command) {
            return new FauxCommand(command);
        }

        public async Task RespondAsync(string content = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None) =>
            await RespondAsyncGettingMessage(content, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);

        public async Task<IUserMessage> RespondAsyncGettingMessage(string content = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            if(_fake)
                return null;

            if(_socketCommandBase is not null && _socketCommandBase.HasResponded)
                return await _socketCommandBase.ModifyOriginalResponseAsync(x => { x.Content = content; x.Embeds = embeds; x.AllowedMentions = allowedMentions; x.Components = components; x.Embed = embed; });
            else if(_socketCommandBase is not null) {
                await _socketCommandBase.RespondAsync(content, embeds, isTTS, ephemeral, allowedMentions, components, embed, options);
                _message = await _socketCommandBase.GetOriginalResponseAsync();
            }
            else
                _message = await _originalMessage.Channel.SendMessageAsync(content, messageReference: new MessageReference(_originalMessage.Id, _originalMessage.Channel.Id));
            return _message;
        }

        public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None) {
            throw new NotSupportedException(nameof(RespondWithFilesAsync));
        }

        public async Task RespondWithFileAsync(FileAttachment attachment, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            if(_fake)
                return;
            if(_socketCommandBase is not null && !_socketCommandBase.HasResponded)
                await _socketCommandBase.RespondWithFileAsync(attachment, text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options);
            else
                await _socketCommandBase.ModifyOriginalResponseAsync(x => { x.Attachments = new List<FileAttachment> { attachment }; x.Content = text ?? ""; x.Embeds = embeds; x.Embed = embed; x.Components = components; });
        }


        public async Task DeleteOriginalResponseAsync(RequestOptions options = null) {
            if(_fake)
                return;
            if(_socketCommandBase is not null)
                await _socketCommandBase.DeleteOriginalResponseAsync(options);
            else
                await _message.DeleteAsync(options);
        }

        public async Task DeferAsync(bool ephemeral = false, RequestOptions options = null) {
            if(_fake)
                return;
            if(_socketCommandBase is not null && !_socketCommandBase.HasResponded)
                await _socketCommandBase.DeferAsync(ephemeral, options);
        }

        public Task RespondWithModalAsync(Modal modal, RequestOptions options = null) {
            throw new NotSupportedException(nameof(RespondWithModalAsync));
        }

        public async Task<IUserMessage> GetOriginalResponseAsync(RequestOptions options = null) {
            return _socketCommandBase is null ? _message : await _socketCommandBase?.GetOriginalResponseAsync(options);
        }
        async Task<IUserMessage> IDiscordInteraction.GetOriginalResponseAsync(RequestOptions options) {
            return await GetOriginalResponseAsync(options);
        }

        public async Task<IUserMessage> ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options = null) {
            if(_fake)
                return null;
            if(_socketCommandBase is not null)
                return await _socketCommandBase.ModifyOriginalResponseAsync(func, options);

            _message ??= await GetOriginalResponseAsync();
            if(_message is null)
                return null;

            await _message.ModifyAsync(func, options);
            return _message;
        }

        async Task<IUserMessage> IDiscordInteraction.ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options) {
            return await ModifyOriginalResponseAsync(func, options);
        }

        public async Task<IUserMessage> ModifyOriginalResponseAsync(string content) {
            return await ModifyOriginalResponseAsync(x => x.Content = content);
        }

        public async Task RespondWithPremiumRequiredAsync(RequestOptions options = null) =>
            await RespondWithPremiumRequiredAsyncReturningMessage(options);

        public async Task<IUserMessage> RespondWithPremiumRequiredAsyncReturningMessage(RequestOptions options = null) {
            return await RespondAsyncGettingMessage("", embed: EmbedHelpers.MakeCustomEmbed(EmbedHelpers.EmbedType.Error, "How did you get here...?", "Nothing in E9K is behind a paywall. If you're seeing this, there's been an error."), options: options);
        }

        public async Task<IUserMessage> RespondWithFilesAsyncGettingMessage(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            if(_fake)
                return null;
            if(_socketCommandBase is not null) {
                if (_socketCommandBase.HasResponded) 
                    return await _socketCommandBase.ModifyOriginalResponseAsync(o => {
                        o.Content = text;
                        o.Embeds = embeds;
                        o.AllowedMentions = allowedMentions;
                        o.Components = components;
                        o.Embed = embed;
                        o.Attachments = attachments.ToList();
                    });
                else {
                    await _socketCommandBase.RespondWithFilesAsync(attachments, text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);
                    return await _socketCommandBase.GetOriginalResponseAsync();
                }
            }
            throw new NotSupportedException(nameof(RespondWithFilesAsyncGettingMessage));
        }

        public async Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) =>
            await RespondWithFilesAsyncGettingMessage(attachments, text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);

        private async Task<IUserMessage> FollowUpAsync(string text = null, IEnumerable<FileAttachment> attachments = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None) {
            if(_fake)
                return null;
            if(_socketCommandBase is not null)
                if (attachments is not null && attachments.Any())
                    return await _socketCommandBase.FollowupWithFilesAsync(attachments, text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll, flags);
                else 
                    return await _socketCommandBase.FollowupAsync(text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll, flags);
            throw new NotSupportedException(nameof(FollowUpAsync));
        }

        public async Task<IUserMessage> FollowupAsync(string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None) {
            return await FollowUpAsync(text, null, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);
        }

        public Task<IUserMessage> FollowupWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null, MessageFlags flags = MessageFlags.None) {
            return FollowUpAsync(text, attachments, embeds, isTTS, ephemeral, allowedMentions, components, embed, options, poll);
        }

        public FauxApplicationCommandData Data {
            get {
                if(_socketUserCommand is not null)
                    return new FauxApplicationCommandData {
                        Name = _socketUserCommand.Data.Name,
                        Options = [.. _socketUserCommand.Data.Options.Select(x => new FauxSocketSlashCommandDataOption(x))]
                    };
                if(_socketSlashCommand is not null)
                    return new FauxApplicationCommandData {
                        Name = _socketSlashCommand.Data.Name,
                        Options = [.. _socketSlashCommand.Data.Options.Select(x => new FauxSocketSlashCommandDataOption(x))]
                    };
                var commandText = CommandRegex().Match(_originalMessage.Content).Groups[1].Value.ToLower();

                if(commandText == "register") {
                    var eggincid = EIRegex().Match(_originalMessage.Content).Value;
                    return new FauxApplicationCommandData {
                        Name = commandText,
                        Options = [
                            new FauxSocketSlashCommandDataOption() {
                                Name = "eggincid",
                                Type = ApplicationCommandOptionType.String, 
                                Value = eggincid
                            }
                        ]
                    };
                }

                

                return new FauxApplicationCommandData { Name = commandText, Options = new List<FauxSocketSlashCommandDataOption>() };
            }
        }

        public class FauxApplicationCommandData {
            public ulong Id => throw new NotSupportedException(nameof(Id));

            public string Name { get; set; }

            public IList<FauxSocketSlashCommandDataOption> Options {
                get; set;
            }
        }

        public class FauxSocketSlashCommandDataOption {
            public string Name { get; set; }
            public ApplicationCommandOptionType Type { get; set; }
            public object Value { get; set; }
            public List<FauxSocketSlashCommandDataOption> Options { get; set; }

            public FauxSocketSlashCommandDataOption() {

            }
            public FauxSocketSlashCommandDataOption(IApplicationCommandInteractionDataOption option) {
                Name = option.Name;
                Type = option.Type;
                Value = option.Value;
                Options = option.Options?.Select(x => new FauxSocketSlashCommandDataOption(x)).ToList() ?? new List<FauxSocketSlashCommandDataOption>();
            }
        }

        public ulong Id {
            get {
                if(_socketCommandBase is not null)
                    return _socketCommandBase.Id;
                return _originalMessage.Id;
            }
        }

        public InteractionType Type {
            get {
                if(_socketCommandBase is not null)
                    return _socketCommandBase.Type;
                return InteractionType.ApplicationCommand;
            }
        }

        public string Token => throw new NotSupportedException(nameof(Token));

        public int Version => throw new NotSupportedException(nameof(Version));

        public bool HasResponded {
            get {
                if(_socketCommandBase is not null)
                    return _socketCommandBase.HasResponded;
                return _message != null;
            }
        }

        public IUser User {
            get {
                return _socketCommandBase?.User ?? _originalMessage.Author;
            }
        }

        public string UserLocale => throw new NotSupportedException(nameof(UserLocale));

        public string GuildLocale => throw new NotSupportedException(nameof(GuildLocale));

        public bool IsDMInteraction {
            get {
                return _socketCommandBase?.IsDMInteraction ?? throw new NotSupportedException(nameof(IsDMInteraction));
            }
        }

        public ulong? ChannelId {
            get {
                return _socketCommandBase?.ChannelId ?? _originalMessage.Channel?.Id;
            }
        }

        public IMessageChannel Channel {
            get {
                return (IMessageChannel)_socketCommandBase?.Channel ?? _originalMessage.Channel;
            }
        }

        public ulong? GuildId {
            get {
                return _socketCommandBase?.GuildId ?? _guildid;
            }
        }

        public ulong ApplicationId => throw new NotSupportedException(nameof(ApplicationId));

        public DateTimeOffset CreatedAt {
            get {
                return _socketCommandBase?.CreatedAt ?? _originalMessage.CreatedAt;
            }
        }

        IDiscordInteractionData IDiscordInteraction.Data => throw new NotSupportedException(nameof(IDiscordInteraction.Data));

        public IReadOnlyCollection<IEntitlement> Entitlements => throw new NotSupportedException(nameof(Entitlements));

        public GuildPermissions Permissions => throw new NotSupportedException(nameof(Permissions));

        public IReadOnlyDictionary<ApplicationIntegrationType, ulong> IntegrationOwners => throw new NotSupportedException(nameof(IntegrationOwners));

        public InteractionContextType? ContextType => throw new NotSupportedException(nameof(ContextType));

        public ulong AttachmentSizeLimit => throw new NotSupportedException(nameof(AttachmentSizeLimit));

        [GeneratedRegex(@"^/(\w+)")]
        private static partial Regex CommandRegex();
        [GeneratedRegex(@"E[I1]\d+")]
        private static partial Regex EIRegex();
    }
}
