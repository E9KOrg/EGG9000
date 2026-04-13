using Discord.WebSocket;

using EGG9000.Common.Database;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EGG9000.Common.Helpers {
    public class FixKevsMistakes {
        public static async Task DeletePrematureContract(ApplicationDbContext _db, DiscordSocketClient _client, ILogger _logger) {

            var contract = await _db.Contracts.Include(x => x.GuildContracts).FirstOrDefaultAsync(x => x.ID == "amazon-burning");

            var coops = await _db.Coops.Where(x => x.ContractID == contract.ID).ToListAsync();


            foreach(var coop in coops) {
                if(coop.ThreadID > 0) {
                    var channel = (SocketThreadChannel)_client.GetChannel(coop.ThreadID);
                    if(channel?.ParentChannel is not null) {
                        _logger.LogInformation("Deleting channel for {channel}", channel.ParentChannel.Name);
                        try {
                            await channel.ParentChannel.DeleteAsync();
                        } catch { }
                    }
                } else {
                    var channel = (SocketTextChannel)_client.GetChannel(coop.DiscordChannelId);
                    if(channel is not null) {
                        _logger.LogInformation("Deleting channel for {channel}", channel.Name);
                        try {
                            await channel.DeleteAsync();
                        } catch { }
                    }
                }
                var xrefs = await _db.UserCoopXrefs.Where(x => x.CoopId == coop.Id).ToListAsync();
                _db.RemoveRange(xrefs);
                _db.Remove(coop);
                _logger.LogInformation("Deleting {coop} from database", coop.Name);
            }

            foreach(var guild in contract.GuildContracts) {
                var channel = (SocketTextChannel)_client.GetChannel(guild.DiscordChannelId);
                if(channel is not null) {
                    _logger.LogInformation("Deleting channel for {channel}", channel.Name);
                    await channel.DeleteAsync();
                }
            }

            _db.RemoveRange(contract.GuildContracts);
            _db.Remove(contract);

            await _db.SaveChangesAsync();
        }
    }
}