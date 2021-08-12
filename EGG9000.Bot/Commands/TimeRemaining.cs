using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Bot.Commands {
    public class TimeRemaining {
        public static async Task ExecuteAsync(SocketMessage message, string[] args) {
            if (args.Length != 3) {
                await message.Channel.SendMessageAsync($@"Invalid arguments: 
!contracttime {{targetEggsShipped}} {{currentEggsShipped}} {{currentShippingRate}}
Ex: !contracttime 50q 22q 600T
");
                return;
            }
            BigInteger targetEggsShipped, currentEggsShipped, currentShippingRate;
            try {
                targetEggsShipped = ArgumentsHelper.NumberFromString(args[0]);
            } catch (UnableToParseNumberExecption) {
                await message.Channel.SendMessageAsync($"Unable to parse argument targetEggsShipped: {args[0]}");
                return;
            }
            try {
                currentEggsShipped = ArgumentsHelper.NumberFromString(args[1]);
            } catch (UnableToParseNumberExecption) {
                await message.Channel.SendMessageAsync($"Unable to parse argument currentEggsShipped: {args[0]}");
                return;
            }
            try {
                currentShippingRate = ArgumentsHelper.NumberFromString(args[2]);
            } catch (UnableToParseNumberExecption) {
                await message.Channel.SendMessageAsync($"Unable to parse argument currentShippingRate: {args[0]}");
                return;
            }

            var difference = targetEggsShipped - currentEggsShipped;

            var totalHours = (Decimal)(difference / currentShippingRate);
            var days = Math.Floor(totalHours / 24m);
            var hours = totalHours - days * 24;

            await message.Channel.SendMessageAsync($"Time Remaining: {days}d {hours}h");
        }
    }
}
