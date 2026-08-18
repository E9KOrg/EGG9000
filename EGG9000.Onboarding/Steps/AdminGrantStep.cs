using EGG9000.Common.Database.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace EGG9000.Onboarding.Steps;

// Polls for a condition to become true. Extracted from AdminGrantStep so the loop is testable with a fake
// lookup and millisecond intervals, without a database and without real waiting.
public static class LoginWaiter {
    // Calls lookup every pollInterval until it returns a non-null value, timeout elapses, or the token is
    // cancelled. Returns null on timeout or cancellation. Never throws OperationCanceledException, because
    // giving up on the wait is a normal outcome, not a failure.
    public static async Task<string> WaitForLoginAsync(
        Func<CancellationToken, Task<string>> lookup,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken) {

        var sw = Stopwatch.StartNew();
        while(true) {
            if(cancellationToken.IsCancellationRequested) return null;

            string found;
            try {
                found = await lookup(cancellationToken);
            } catch(OperationCanceledException) {
                return null;
            }
            if(found is not null) return found;

            if(sw.Elapsed >= timeout) return null;

            try {
                await Task.Delay(pollInterval, cancellationToken);
            } catch(OperationCanceledException) {
                return null;
            }
        }
    }
}

// Grants the Admin role to the operator's Discord account. AspNetUserRoles has a foreign key to
// AspNetUsers, and that row is written only by the Discord OAuth callback in the Site. Onboard cannot
// perform a browser OAuth flow, so when no login row exists yet it prints instructions and waits for one to
// appear. Waiting is never an error. Timeout and Ctrl-C both report Skipped so the run exits 0, because
// every exit must leave a command the operator can simply run again.
public sealed class AdminGrantStep : IOnboardStep {
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    // The scheme name AddDiscord registers in EGG9000.Site/Program.cs, which is what the OAuth
    // callback writes into AspNetUserLogins.LoginProvider.
    private const string DiscordLoginProvider = "Discord";

    // EGG9000.Site/Program.cs calls UseUrls("http://0.0.0.0:5013"), which overrides launchSettings,
    // so the port is the same however the site is started.
    private const string SiteUrl = "http://localhost:5013";

    public string Name => "Admin grant";

    public async Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        // --no-wait means "this run must never block on a human", so it has to suppress the prompt as
        // well as the login wait. Checking it only at the wait would leave a scripted run without
        // --admin sitting in Console.ReadLine forever on any host with a live stdin.
        var nonInteractive = context.Options.NoWait || Console.IsInputRedirected;

        var discordId = context.Options.AdminDiscordId;
        if(discordId is null) {
            if(nonInteractive) {
                return OnboardResult.Skipped("no Discord user id supplied, re-run with --admin <id>");
            }
            discordId = PromptForDiscordId(context);
        }
        if(discordId is null) {
            return OnboardResult.Skipped("no Discord user id supplied, re-run with --admin <id>");
        }

        var key = discordId.Value.ToString();

        var userId = await FindUserIdAsync(context, key, cancellationToken);
        if(userId is null) {
            if(nonInteractive) {
                return OnboardResult.Skipped(
                    $"no site login for {key} yet, log in at {SiteUrl} then run setup again");
            }

            PrintWaitInstructions(context, key);
            userId = await LoginWaiter.WaitForLoginAsync(
                ct => FindUserIdAsync(context, key, ct), WaitTimeout, PollInterval, cancellationToken);

            if(userId is null) {
                return OnboardResult.Skipped(
                    $"still no site login for {key}, run setup again once you have logged in");
            }
            context.Output.WriteLine();
            context.Output.WriteLine("  Login detected.");
        }

        using var scope = context.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId);
        if(user is null) {
            return OnboardResult.Failed($"AspNetUserLogins references user {userId} but no such user exists");
        }

        if(await users.IsInRoleAsync(user, OnboardRoles.Admin)) {
            return OnboardResult.AlreadyExisted($"{key} is already {OnboardRoles.Admin}");
        }

        var added = await users.AddToRoleAsync(user, OnboardRoles.Admin);
        if(!added.Succeeded) {
            var errors = string.Join("; ", added.Errors.Select(e => e.Description));
            return OnboardResult.Failed($"could not grant {OnboardRoles.Admin} to {key}: {errors}");
        }

        return OnboardResult.Created($"{key} is now {OnboardRoles.Admin}");
    }

    // Asks for the operator's Discord id, re-prompting on anything unparseable, the same way GuildSelector
    // handles its prompt. A typo must not end a run that has already applied migrations and seeded a guild.
    // Returns null only when input is unavailable, which is the one case that cannot be recovered by asking
    // again.
    private static ulong? PromptForDiscordId(OnboardContext context) {
        context.Output.WriteLine();
        while(true) {
            context.Output.Write("  Your Discord user id (Developer Mode on, right-click yourself, Copy User ID): ");
            var answer = context.ReadLine();
            if(answer is null) return null;

            if(ulong.TryParse(answer.Trim(), out var parsed)) return parsed;

            context.Output.WriteLine($"  '{answer.Trim()}' is not a numeric Discord user id.");
        }
    }

    // Identity user id for a Discord provider key, or null when that login does not exist yet.
    private static async Task<string> FindUserIdAsync(OnboardContext context, string providerKey, CancellationToken cancellationToken) {
        await using var db = await context.DbFactory.CreateDbContextAsync(cancellationToken);
        // AspNetUserLogins is keyed on (LoginProvider, ProviderKey), so ProviderKey alone is not
        // unique. Filtering on the provider too means adding a second external login later cannot
        // make this grant Admin to whichever user happens to share the key value.
        return await db.UserLogins
            .Where(l => l.LoginProvider == DiscordLoginProvider && l.ProviderKey == providerKey)
            .Select(l => l.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void PrintWaitInstructions(OnboardContext context, string key) {
        context.Output.WriteLine();
        context.Output.WriteLine($"        No site login found for {key} yet.");
        context.Output.WriteLine();
        context.Output.WriteLine("        In another terminal:");
        context.Output.WriteLine("            cd EGG9000.Site");
        context.Output.WriteLine("            dotnet watch --no-hot-reload --configuration DEV9002");
        context.Output.WriteLine();
        context.Output.WriteLine($"        Then open {SiteUrl} and log in with Discord.");
        context.Output.WriteLine();
        context.Output.WriteLine("        Waiting... (Ctrl-C to stop; re-running setup resumes here)");
    }
}
