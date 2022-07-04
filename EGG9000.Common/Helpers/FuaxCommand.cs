using Discord;
using Discord.Rest;
using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public class FauxCommand : IDiscordInteraction {
        SocketMessage _originalMessage;
        SocketSlashCommand _socketSlashCommand;

        RestUserMessage _message;
        ulong? _guildid;

        public FauxCommand(SocketMessage message, ulong? guildid) {
            _originalMessage = message;
            _guildid = guildid;
        }
        public FauxCommand(SocketSlashCommand command) {
            _socketSlashCommand = command;
        }
        public static implicit operator FauxCommand(SocketSlashCommand command) {
            return new FauxCommand(command);
        }


        public async Task RespondAsync(string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            if(_socketSlashCommand is not null)
                await _socketSlashCommand.RespondAsync(text, embeds, isTTS, ephemeral, allowedMentions, components, embed, options);
            else
                _message = await _originalMessage.Channel.SendMessageAsync(text, messageReference: new MessageReference(_originalMessage.Id, _originalMessage.Channel.Id));
        }

        public Task RespondWithFilesAsync(IEnumerable<FileAttachment> attachments, string text = null, Embed[] embeds = null, bool isTTS = false, bool ephemeral = false, AllowedMentions allowedMentions = null, MessageComponent components = null, Embed embed = null, RequestOptions options = null) {
            throw new NotImplementedException();
        }


        public async Task DeleteOriginalResponseAsync(RequestOptions options = null) {
            if(_socketSlashCommand is not null)
                await _socketSlashCommand.DeleteOriginalResponseAsync(options);
            else
                await _message.DeleteAsync(options);
        }

        public Task DeferAsync(bool ephemeral = false, RequestOptions options = null) {
            throw new NotImplementedException();
        }

        public Task RespondWithModalAsync(Modal modal, RequestOptions options = null) {
            throw new NotImplementedException();
        }

        Task<IUserMessage> IDiscordInteraction.FollowupAsync(string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options) {
            throw new NotImplementedException();
        }

        Task<IUserMessage> IDiscordInteraction.FollowupWithFilesAsync(IEnumerable<FileAttachment> attachments, string text, Embed[] embeds, bool isTTS, bool ephemeral, AllowedMentions allowedMentions, MessageComponent components, Embed embed, RequestOptions options) {
            throw new NotImplementedException();
        }

        public async Task<IUserMessage> GetOriginalResponseAsync(RequestOptions options = null) {
            return await _socketSlashCommand?.GetOriginalResponseAsync(options) ?? _message;
        }
        async Task<IUserMessage> IDiscordInteraction.GetOriginalResponseAsync(RequestOptions options) {
            return await GetOriginalResponseAsync(options);
        }

        public async Task<IUserMessage> ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options = null) {
            if(_socketSlashCommand is not null)
                return await _socketSlashCommand.ModifyOriginalResponseAsync(func, options);
            await _message.ModifyAsync(func, options);
            return _message;
        }

        async Task<IUserMessage> IDiscordInteraction.ModifyOriginalResponseAsync(Action<MessageProperties> func, RequestOptions options) {
            return await ModifyOriginalResponseAsync(func, options);
        }

        public FauxApplicationCommandData Data {
            get {
                if(_socketSlashCommand is not null)
                    return new FauxApplicationCommandData {
                        Name = _socketSlashCommand.Data.Name,
                        Options = _socketSlashCommand.Data.Options.ToList()
                    };
                var commandText = new Regex(@"^/(\w+)").Matches(_originalMessage.Content)[1].Value.ToLower();
                return new FauxApplicationCommandData { Name = commandText };
            }
        }

        public class FauxApplicationCommandData {
            public ulong Id {
                get {
                    throw new NotImplementedException();
                }
            }

            public string Name { get; set; }

            public IList<SocketSlashCommandDataOption> Options {
                get; set;
            }
        }

        public ulong Id {
            get {
                if(_socketSlashCommand is not null)
                    return _socketSlashCommand.Id;
                return _originalMessage.Id;
            }
        }

        public InteractionType Type {
            get {
                if(_socketSlashCommand is not null)
                    return _socketSlashCommand.Type;
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
                if(_socketSlashCommand is not null)
                    return _socketSlashCommand.HasResponded;
                return _message != null;
            }
        }

        public IUser User {
            get {
                return _socketSlashCommand?.User ?? _originalMessage.Author;
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
                return _socketSlashCommand?.ChannelId ?? _originalMessage.Channel?.Id;
            }
        }

        public IMessageChannel Channel {
            get {
                return (IMessageChannel)_socketSlashCommand?.Channel ?? _originalMessage.Channel;
            }
        }

        public ulong? GuildId {
            get {
                return _socketSlashCommand?.GuildId ?? _guildid;
            }
        }

        public ulong ApplicationId {
            get {
                throw new NotImplementedException();
            }
        }

        public DateTimeOffset CreatedAt {
            get {
                return _socketSlashCommand?.CreatedAt ?? _originalMessage.CreatedAt;
            }
        }

        IDiscordInteractionData IDiscordInteraction.Data {
            get {
                throw new NotImplementedException();
            }
        }
    }
}
