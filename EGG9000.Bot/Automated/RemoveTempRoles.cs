using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    public class RemoveTempRoles(IServiceProvider provider) : _UpdaterBase<RemoveTempRoles>(_updateInterval, TimeSpan.Zero, provider) {
        public static readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(5);

        public async override Task Run(object state, CancellationToken cancellationToken) {
            var _db = _provider.CreateScope().ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rolesToRemove = await _db.TemporaryRoles.Where(x => x.Expires < DateTimeOffset.UtcNow && !x.IsRemoved).ToListAsync(CancellationToken.None);
            foreach(var role in rolesToRemove) {
                try {
                    var user = _client.Guilds.First(g => g.Id == role.GuildId).GetUser(role.UserId);
                    await user.RemoveRoleAsync(role.RoleId);
                } catch(Exception ex) {
                    _logger.LogError(ex, "⚠️ERROR: Unable to remove role from user with id {userid}", role.UserId);
                }
                role.IsRemoved = true;
            }
            await _db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
