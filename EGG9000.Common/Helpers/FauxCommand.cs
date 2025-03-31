// Ignore Spelling: Faux

using Discord;
using Discord.Rest;
using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Common.Services {
    public class FauxCommand : IDiscordInteraction {
        SocketMessage _originalMessage;
        SocketSlashCommand _socketSlashCommand;
        SocketUserCommand _socketUserCommand;
        SocketCommandBase _socketCommandBase;

        RestUserMessage _message;
        Boolean _fake = false;

        ulong? _guildid;

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


        public static implicit operator FauxCommand(SocketSlashCommand command) {
            return new FauxCommand(command);
        }

        public static implicit operator FauxCommand(SocketUserCommand command) {
            return new FauxCommand(command);
        }


        public async Task RespondAsync(string content = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            if(_fake)
                return;

            //public async Task RespondAsync(string content = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            if(_socketCommandBase is not null && _socketCommandBase.HasResponded)
                await _socketCommandBase.ModifyOriginalResponseAsync(x => { x.Content = content; x.Embeds = embeds; x.AllowedMentions = allowedMentions; x.Components = components; x.Embed = embed; });
            else if(_socketCommandBase is not null)
                await _socketCommandBase.RespondAsync(content, embeds, isTTS, ephemeral, allowedMentions, components, embed, options);
            else
                _message = await _originalMessage.Channel.SendMessageAsync(content, messageReference: new MessageReference(_originalMessage.Id, _originalMessage.Channel.Id));
        }

        public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            throw new NotImplementedException();
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
            throw new NotImplementedException();
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
            await _message.ModifyAsync(func, options);
            return _message;
        }

        async Task<IUserMessage> IDiscordInteraction.ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options) {
            return await ModifyOriginalResponseAsync(func, options);
        }

        public async Task<IUserMessage> ModifyOriginalResponseAsync(string content) {
            return await ModifyOriginalResponseAsync(x => x.Content = content);
        }

        public Task RespondWithPremiumRequiredAsync(RequestOptions options = null) {
            throw new NotImplementedException();
        }


        public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            throw new NotImplementedException();
        }

        public Task<IUserMessage> FollowupAsync(string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            throw new NotImplementedException();
        }

        public Task<IUserMessage> FollowupWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null, PollProperties poll = null) {
            throw new NotImplementedException();
        }

        public FauxApplicationCommandData Data {
            get {
                if(_socketUserCommand is not null)
                    return new FauxApplicationCommandData {
                        Name = _socketUserCommand.Data.Name,
                        Options = _socketUserCommand.Data.Options.Select(x => new FauxSocketSlashCommandDataOption(x)).ToList()
                    };
                if(_socketSlashCommand is not null)
                    return new FauxApplicationCommandData {
                        Name = _socketSlashCommand.Data.Name,
                        Options = _socketSlashCommand.Data.Options.Select(x => new FauxSocketSlashCommandDataOption(x)).ToList()
                    };
                var commandText = new Regex(@"^/(\w+)").Match(_originalMessage.Content).Groups[1].Value.ToLower();

                if(commandText == "register") {
                    var eggincid = new Regex(@"E[I1]\d+").Match(_originalMessage.Content).Value;
                    return new FauxApplicationCommandData {
                        Name = commandText,
                        Options = new List<FauxSocketSlashCommandDataOption>() {
                            new FauxSocketSlashCommandDataOption() { 
                                Name = "eggincid", 
                                Type = ApplicationCommandOptionType.String, 
                                Value = eggincid
                            }
                        }
                    };
                }

                

                return new FauxApplicationCommandData { Name = commandText, Options = new List<FauxSocketSlashCommandDataOption>() };
            }
        }

        public class FauxApplicationCommandData {
            public ulong Id {
                get {
                    throw new NotImplementedException();
                }
            }

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

        public string Token {
            get {
                throw new NotImplementedException();
            }
        }

        public int Version {
            get {
                throw new NotImplementedException();
            }
        }

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

        public string UserLocale {
            get {
                throw new NotImplementedException();
            }
        }

        public string GuildLocale {
            get {
                throw new NotImplementedException();
            }
        }

        public bool IsDMInteraction {
            get {
                throw new NotImplementedException();
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

        public ulong ApplicationId {
            get {
                throw new NotImplementedException();
            }
        }

        public DateTimeOffset CreatedAt {
            get {
                return _socketCommandBase?.CreatedAt ?? _originalMessage.CreatedAt;
            }
        }

        IDiscordInteractionData IDiscordInteraction.Data {
            get {
                throw new NotImplementedException();
            }
        }

        public IReadOnlyCollection<IEntitlement> Entitlements {
            get {
                throw new NotImplementedException();
            }
        }

        //public IReadOnlyDictionary<ApplicationIntegrationType, ulong> IntegrationOwners {
        //    get {
        //        throw new NotImplementedException();
        //    }
        //}

        //public InteractionContextType? ContextType {
        //    get {
        //        throw new NotImplementedException();
        //    }
        //}

        public GuildPermissions Permissions {
            get {
                throw new NotImplementedException();
            }
        }

        public IReadOnlyDictionary<ApplicationIntegrationType, ulong> IntegrationOwners {
            get {
                throw new NotImplementedException();
            }
        }

        public InteractionContextType? ContextType {
            get {
                throw new NotImplementedException();
            }
        }
    }
}
