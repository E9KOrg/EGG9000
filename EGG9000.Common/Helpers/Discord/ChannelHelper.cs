using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Discord;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace EGG9000.Bot.Common.Helpers {
    public class ChannelHelper {
        public static object DetermineChannelType(Guild dbGuild, SocketGuild discordGuild, GuildChannelType channelType) {

            //Null checks
            if(dbGuild is null || discordGuild is null) return null;
            var channelDetails = dbGuild.ChannelDetails.FirstOrDefault(d => d.ChannelType == channelType);
            if(channelDetails is null) return null;

            //Thread
            var thread = discordGuild.GetThreadChannel(channelDetails.Id);
            if(thread is not null) return thread;

            //Channel
            var channel = discordGuild.GetTextChannel(channelDetails.Id);
            if(channel is not null) return channel;

            //Null
            return null;
        }

        public class CustomDiscordMessage() {
            public string Text { get; set; } = null;
            public bool IsTTS { get; set; } = false;
            public Embed Embed { get; set; } = null;
            public RequestOptions Options { get; set; } = null;
            public AllowedMentions AllowedMentions { get; set; } = null;
            public MessageReference MessageReference { get; set; } = null;
            public MessageComponent Components { get; set; } = null;
            public ISticker[] Stickers { get; set; } = null;
            public Embed[] Embeds { get; set; } = null;
            public MessageFlags Flags { get; set; } = MessageFlags.None;
        }

        public static async Task<Discord.Rest.RestUserMessage> DetermineAndSend(Guild dbGuild, SocketGuild discordGuild, GuildChannelType channelType, CustomDiscordMessage message, ILogger logger = null) {

            var channel = DetermineChannelType(dbGuild, discordGuild, channelType);
            if(channel is null ) return null;

            if(channel.GetType() == typeof(SocketThreadChannel)) {
                return await ((SocketThreadChannel)channel).SendMessageAsync(message.Text, message.IsTTS, message.Embed, message.Options, message.AllowedMentions, message.MessageReference, message.Components, message.Stickers, message.Embeds, message.Flags);
            } else if(channel.GetType() == typeof(SocketTextChannel)) {
                return await ((SocketTextChannel)channel).SendMessageAsync(message.Text, message.IsTTS, message.Embed, message.Options, message.AllowedMentions, message.MessageReference, message.Components, message.Stickers, message.Embeds, message.Flags);
            } else {
                if(logger is not null) logger.LogWarning("DetermineAndSend called, expected type of SocketTextChannel or SocketThreadChannel. Instead found type of " + channel.GetType());
                return null;
            }
        }
    }
}
