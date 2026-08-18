using Discord;
using Discord.WebSocket;
using EGG9000.Onboarding.Steps;
using EGG9000.Common.Database;
using EGG9000.Common.Database.Entities;
using EGG9000.Common.Helpers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding;

// Runs the setup steps for a fresh instance and reports what each one did. Registers only the handful of
// services the steps need rather than reusing the bot's or the site's host wiring, both of which start
// background work that has no place in a setup run.
public static class OnboardCommand {
    public const int ExitSuccess = 0;
    public const int ExitFailure = 1;

    public static string ProductionGuardMessage =>
        "Setup writes to the database and DEV9001 and RELEASE point at the live production "
        + "database. Run it under DEV9002 or Debug instead, for example: "
        + "dotnet run --configuration DEV9002";

    // True when the active build configuration points at the production database.
    public static bool IsBlockedConfiguration() => BuildConfig.IsDev9001 || BuildConfig.IsRelease;

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, CancellationToken cancellationToken) {
        var output = Console.Out;

        if(IsBlockedConfiguration()) {
            output.WriteLine();
            output.WriteLine("  Refusing to run.");
            output.WriteLine($"  {ProductionGuardMessage}");
            output.WriteLine();
            return ExitFailure;
        }

        OnboardOptions options;
        try {
            options = OnboardOptions.Parse(args);
        } catch(ArgumentException ex) {
            output.WriteLine($"  {ex.Message}");
            return ExitFailure;
        }

        var connectionString = SecretsHelper.GetConfigOrSecret(
            configuration, "ConnectionStrings:DefaultConnection", "db_connection_string");
        var token = SecretsHelper.GetConfigOrSecret(configuration, "ConnectionStrings:Token", "token");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        // Both registrations are needed: the steps use the factory, Identity's EF stores use the
        // scoped context.
        services.AddDbContextFactory<ApplicationDbContext>(o =>
            o.UseNpgsql(connectionString, x => x.MigrationsAssembly("EGG9000.Common")));
        services.AddDbContext<ApplicationDbContext>(o =>
            o.UseNpgsql(connectionString, x => x.MigrationsAssembly("EGG9000.Common")));
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();

        // Unprivileged intents only. Setup reads nothing but each guild's id and name, which arrive
        // with GUILD_CREATE. Asking for GuildMembers would make a first run fail with a 4014
        // disconnect for anyone who has not enabled Server Members Intent in the developer portal.
        using var discord = new DiscordSocketClient(new DiscordSocketConfig {
            GatewayIntents = GatewayIntents.AllUnprivileged,
            AlwaysDownloadUsers = false
        });

        var context = new OnboardContext {
            Configuration = configuration,
            Options = options,
            DbFactory = provider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>(),
            Discord = discord,
            Services = provider,
            Output = output,
            ReadLine = Console.ReadLine
        };

        List<IOnboardStep> steps = [
            new SecretsPreflightStep(),
            new MigrationsStep(),
            new GuildSelectionStep(),
            new GuildRowStep(),
            new DevGuildStep(),
            new AdminRoleStep(),
            new AdminGrantStep()
        ];

        output.WriteLine();

        // Ctrl-C is a deliberate interrupt, not a failure. Every step is idempotent, so the only
        // correct response is to say where the run stopped and exit 0 on a command the operator can
        // simply repeat.
        try {
            // The preflight must run before the Discord connection, so a missing token is reported as
            // a missing key rather than as a login failure.
            var preflight = await RunStepAsync(steps[0], 1, steps.Count, context, output, cancellationToken);
            if(preflight.Outcome == OnboardOutcome.Failed) {
                return Finish(output, ExitFailure);
            }

            // Subscribed before StartAsync, never after. Ready can fire while the connection is being
            // established, and a handler attached afterwards would miss it and wait out the timeout.
            // RunContinuationsAsynchronously, or every remaining step continues inline inside
            // Discord.Net's Ready dispatch: the prompts and the ten-minute login poll would all run
            // on the gateway's handler, which its watchdog treats as a blocked event.
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task OnReady() { ready.TrySetResult(); return Task.CompletedTask; }
            discord.Ready += OnReady;

            try {
                await discord.LoginAsync(TokenType.Bot, token);
                await discord.StartAsync();
            } catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                discord.Ready -= OnReady;
                throw;
            } catch(Exception ex) {
                discord.Ready -= OnReady;
                output.WriteLine($"  Discord login failed: {ex.Message}");
                output.WriteLine("  Check ConnectionStrings:Token in your secrets.json.");
                return Finish(output, ExitFailure);
            }

            try {
                // Separate from the login above so a gateway problem is not diagnosed as a bad token.
                await WaitForGuildCacheAsync(ready.Task, cancellationToken);
            } catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
                // Ctrl-C, not a timeout. This wait can last 32 seconds, which is exactly when an
                // operator gives up, so it must reach the interrupt handler rather than be reported
                // as a broken gateway.
                throw;
            } catch(OperationCanceledException) {
                output.WriteLine("  The Discord gateway did not become ready within 30 seconds.");
                output.WriteLine("  The token was accepted, so this is a connection problem rather than a bad token.");
                return Finish(output, ExitFailure);
            } catch(Exception ex) {
                output.WriteLine($"  The Discord gateway did not become ready: {ex.Message}");
                return Finish(output, ExitFailure);
            } finally {
                discord.Ready -= OnReady;
            }

            try {
                for(var i = 1; i < steps.Count; i++) {
                    var result = await RunStepAsync(steps[i], i + 1, steps.Count, context, output, cancellationToken);
                    if(result.Outcome == OnboardOutcome.Failed) {
                        return Finish(output, ExitFailure);
                    }
                }
            } finally {
                await discord.StopAsync();
                await discord.LogoutAsync();
            }
        } catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            output.WriteLine();
            output.WriteLine("  Interrupted. Nothing is half-finished; run setup again to carry on.");
            return Finish(output, ExitSuccess);
        }

        // The admin grant step swallows cancellation by design and reports Skipped, so a Ctrl-C during
        // its login wait never throws and would otherwise fall through to "Done" and read as a
        // completed setup. It is the last step and the only one that waits, which makes this the case
        // the interrupt handling above exists for.
        if(cancellationToken.IsCancellationRequested) {
            output.WriteLine();
            output.WriteLine("  Interrupted. Nothing is half-finished; run setup again to carry on.");
            return Finish(output, ExitSuccess);
        }

        output.WriteLine();
        output.WriteLine("  Done. Start the bot and site.");
        return Finish(output, ExitSuccess);
    }

    private static async Task<OnboardResult> RunStepAsync(
        IOnboardStep step, int index, int total, OnboardContext context, System.IO.TextWriter output, CancellationToken cancellationToken) {

        var label = $"  [{index}/{total}] {step.Name} ".PadRight(38, '.');
        output.Write(label);

        OnboardResult result;
        try {
            result = await step.RunAsync(context, cancellationToken);
        } catch(OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            // Ctrl-C. Let it reach the handler in RunAsync rather than reporting the step as failed,
            // which would print a stack-trace-shaped error for something the operator chose to do.
            output.WriteLine(" interrupted");
            throw;
        } catch(Exception ex) {
            // A step throwing is a failed step, not a crashed command. The operator gets the message
            // and a nonzero exit, not a stack trace. Npgsql connection failures name the host here.
            output.WriteLine(" failed");
            output.WriteLine($"        {ex.Message}");
            return OnboardResult.Failed(ex.Message);
        }

        var word = result.Outcome switch {
            OnboardOutcome.Created => "created",
            OnboardOutcome.AlreadyExisted => "ok",
            OnboardOutcome.Skipped => "skipped",
            _ => "failed"
        };
        output.WriteLine($" {word}");
        if(!string.IsNullOrWhiteSpace(result.Detail)) {
            output.WriteLine($"        {result.Detail}");
        }
        return result;
    }

    // The gateway populates the guild cache asynchronously after Ready, so a short settle window
    // avoids listing an empty server list on a cold connection. The caller owns the subscription,
    // which must be in place before StartAsync for the event to be observable at all.
    private static async Task WaitForGuildCacheAsync(Task ready, CancellationToken cancellationToken) {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await ready.WaitAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
    }

    private static int Finish(System.IO.TextWriter output, int exitCode) {
        output.WriteLine();
        return exitCode;
    }
}
