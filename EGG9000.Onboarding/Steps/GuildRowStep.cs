using EGG9000.Common.Database.Entities;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Creates the Guilds row the bot needs for the selected Discord server. Sets three fields and lets EF Core
// supply every other column from the entity defaults, exactly as AdminController.AddGuildToDb does. This is
// deliberate: the README previously carried a raw SQL INSERT naming every NOT NULL column, which drifted
// every time Guild gained a property. Do not enumerate additional columns here. An existing row is left
// untouched, including its Name, because operators configure guilds through /Admin/ConfigureServer and
// onboard must never clobber that.
public sealed class GuildRowStep : IOnboardStep {
    public string Name => "Guild row";

    public async Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        await using var db = await context.DbFactory.CreateDbContextAsync(cancellationToken);

        var id = context.SelectedGuildId;
        var existing = await db.Guilds.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if(existing is not null) {
            return OnboardResult.AlreadyExisted($"{existing.Name} ({id})");
        }

        db.Guilds.Add(new Guild {
            Id = id,
            DiscordSeverId = id,
            Name = context.SelectedGuildName
        });
        await db.SaveChangesAsync(cancellationToken);

        return OnboardResult.Created($"{context.SelectedGuildName} ({id})");
    }
}
