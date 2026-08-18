using EGG9000.Onboarding;
using EGG9000.Onboarding.Steps;
using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Test.Integration;

// The per-step tests each prove one step is idempotent on its own. This proves they compose:
// running the whole sequence twice leaves identical state and reports nothing as Created on the
// second pass. That is the property the onboard command actually promises operators.
[TestClass]
[TestCategory("Integration")]
public class OnboardSequenceTests {
    private const ulong SequenceGuildId = 665544332211009988UL;
    private const string SequenceDiscordId = "224466880011223344";

    // GuildSelectionStep needs a live Discord gateway, so it is excluded and its output is set
    // directly on the context, exactly as that step would set it.
    private static List<IOnboardStep> DatabaseSteps() => [
        new MigrationsStep(),
        new GuildRowStep(),
        new AdminRoleStep(),
        new AdminGrantStep()
    ];

    // Stands in for the Discord OAuth callback, which is the only thing that writes these rows in
    // production. AdminGrantStep looks the user up by AspNetUserLogins.ProviderKey.
    private static async Task SeedSiteLoginAsync(ServiceProvider provider, CancellationToken ct) {
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByNameAsync(SequenceDiscordId);
        if(existing is not null) return;

        var user = new ApplicationUser { UserName = SequenceDiscordId, Email = $"{SequenceDiscordId}@example.invalid" };
        var created = await users.CreateAsync(user);
        Assert.IsTrue(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        var linked = await users.AddLoginAsync(user, new UserLoginInfo("Discord", SequenceDiscordId, "Discord"));
        Assert.IsTrue(linked.Succeeded, string.Join("; ", linked.Errors.Select(e => e.Description)));
    }

    [TestMethod]
    public async Task Sequence_RunTwice_SecondPassCreatesNothing() {
        await using var provider = AdminRoleStepTests.BuildProvider();
        var ct = TestContext!.CancellationToken;

        var ctx = AdminRoleStepTests.Context(provider, ["--onboard", "--admin", SequenceDiscordId, "--no-wait"]);
        ctx.SelectedGuildId = SequenceGuildId;
        ctx.SelectedGuildName = "Sequence Server";

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(ct)) {
            await db.Database.MigrateAsync(ct);
            await db.Guilds.Where(g => g.Id == SequenceGuildId).ExecuteDeleteAsync(ct);
        }
        await SeedSiteLoginAsync(provider, ct);

        foreach(var step in DatabaseSteps()) {
            var result = await step.RunAsync(ctx, ct);
            Assert.AreNotEqual(OnboardOutcome.Failed, result.Outcome, $"{step.Name}: {result.Detail}");
        }

        foreach(var step in DatabaseSteps()) {
            var result = await step.RunAsync(ctx, ct);
            Assert.AreNotEqual(OnboardOutcome.Created, result.Outcome,
                $"{step.Name} was not idempotent, it reported Created on the second pass: {result.Detail}");
            Assert.AreNotEqual(OnboardOutcome.Failed, result.Outcome, $"{step.Name}: {result.Detail}");
        }
    }

    // The grant step must never invent an Identity user of its own. Only the site's OAuth callback
    // may create one, so with no login present the step has to report Skipped and write nothing.
    [TestMethod]
    public async Task AdminGrant_NoLoginAndNoWait_SkipsAndWritesNothing() {
        await using var provider = AdminRoleStepTests.BuildProvider();
        var ct = TestContext!.CancellationToken;

        var ctx = AdminRoleStepTests.Context(provider, ["--onboard", "--admin", "999000111222333444", "--no-wait"]);

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(ct)) {
            await db.Database.MigrateAsync(ct);
        }
        await new AdminRoleStep().RunAsync(ctx, ct);

        var result = await new AdminGrantStep().RunAsync(ctx, ct);

        Assert.AreEqual(OnboardOutcome.Skipped, result.Outcome, result.Detail);
        StringAssert.Contains(result.Detail, "localhost:5013");

        await using(var db = await ctx.DbFactory.CreateDbContextAsync(ct)) {
            var orphan = await db.UserLogins.AnyAsync(l => l.ProviderKey == "999000111222333444", ct);
            Assert.IsFalse(orphan, "The grant step must not create an Identity user of its own.");
        }
    }

    public TestContext? TestContext { get; set; }
}
