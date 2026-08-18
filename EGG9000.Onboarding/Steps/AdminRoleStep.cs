using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Ensures the Admin IdentityRole exists. Mirrors the GuildReadOnlyAdmin seeder in EGG9000.Site/Program.cs,
// using RoleManager rather than raw SQL against AspNetRoles so the normalized name and concurrency stamp
// are produced by Identity itself.
public sealed class AdminRoleStep : IOnboardStep {
    public string Name => "Admin role";

    public async Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        // RoleManager is scoped, so it must be resolved from a scope rather than the root provider.
        using var scope = context.Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        if(await roles.RoleExistsAsync(OnboardRoles.Admin)) {
            return OnboardResult.AlreadyExisted(OnboardRoles.Admin);
        }

        var created = await roles.CreateAsync(new IdentityRole(OnboardRoles.Admin));
        if(!created.Succeeded) {
            var errors = string.Join("; ", created.Errors.Select(e => e.Description));
            return OnboardResult.Failed($"could not create the {OnboardRoles.Admin} role: {errors}");
        }

        return OnboardResult.Created(OnboardRoles.Admin);
    }
}
