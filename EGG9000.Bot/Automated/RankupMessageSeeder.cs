using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Automated {
    /// <summary>
    /// One-shot startup seeder: if the palace guild has no <see cref="RankupMessage"/> rows yet,
    /// inserts the default rank-up messages (the formerly hardcoded ones) so guilds that author
    /// nothing keep identical-flavor announcements. Idempotent - skips once the palace has any rows.
    /// </summary>
    public class RankupMessageSeeder(IServiceScopeFactory scopeFactory, ILogger<RankupMessageSeeder> logger) : IHostedService {
        public async Task StartAsync(CancellationToken cancellationToken) {
            try {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var palace = await db.GetPalaceGuildAsync();
                if(await db.RankupMessages.AnyAsync(m => m.GuildId == palace.Id, cancellationToken)) return;

                var count = 0;
                foreach(var (groupBaseOom, text) in RankupMessageSeed.Defaults()) {
                    db.RankupMessages.Add(new RankupMessage {
                        InternalId = Guid.NewGuid().ToString("N"),
                        GuildId = palace.Id,
                        GuildName = palace.Name,
                        GroupBaseOom = groupBaseOom,
                        Text = text,
                        Weight = 1,
                        CreatedBy = "System"
                    });
                    count++;
                }
                await db.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Seeded {Count} default palace rank-up messages", count);
            } catch(Exception e) {
                logger.LogWarning(e, "Skipped rank-up message seeding (palace guild not available or DB error)");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
