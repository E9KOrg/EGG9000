using Discord.WebSocket;
using EGG9000.Common.Database.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated.Helpers {
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
    }
}
