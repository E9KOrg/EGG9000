using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Applies pending EF Core migrations. EGG9000.Bot/Program.cs deliberately keeps automatic migration to
// RELEASE only, because dev configurations run against shared or live databases and a half-written
// migration must not auto-apply. That still holds: this runs only because the operator explicitly ran
// setup, and setup refuses to run under DEV9001 or RELEASE.
public sealed class MigrationsStep : IOnboardStep {
    public string Name => "Migrations";

    public async Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        await using var db = await context.DbFactory.CreateDbContextAsync(cancellationToken);

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if(pending.Count == 0) {
            return OnboardResult.AlreadyExisted("database already current");
        }

        await db.Database.MigrateAsync(cancellationToken);
        return OnboardResult.Created($"{pending.Count} applied");
    }
}
