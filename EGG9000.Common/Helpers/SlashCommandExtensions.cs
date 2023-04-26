using Discord.WebSocket;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EGG9000.Common.Services;

namespace EGG9000.Common.Helpers {
    public static class SlashCommandExtensions {
        public static async Task DeleteResponseFix(this FauxCommand command) {
            if(command == null)
                return;
            var response = await command.GetOriginalResponseAsync();
            if(response == null)
                return;
            await response.DeleteAsync();
        }
    }
}
