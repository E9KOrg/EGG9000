using Discord.WebSocket;
using EGG9000.Common.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding;

// What a step did. Every step is idempotent, so a second run reports AlreadyExisted.
public enum OnboardOutcome {
    Created,
    AlreadyExisted,
    // Not done, and that is acceptable. Never an error. See Detail for why.
    Skipped,
    Failed
}

// Outcome is what happened; Detail is the one line shown next to the step name in the report.
public sealed record OnboardResult(OnboardOutcome Outcome, string Detail) {
    public static OnboardResult Created(string detail) => new(OnboardOutcome.Created, detail);
    public static OnboardResult AlreadyExisted(string detail) => new(OnboardOutcome.AlreadyExisted, detail);
    public static OnboardResult Skipped(string detail) => new(OnboardOutcome.Skipped, detail);
    public static OnboardResult Failed(string detail) => new(OnboardOutcome.Failed, detail);
}

// Command-line options for a setup run, parsed from the raw process args.
public sealed class OnboardOptions {
    // Guild to seed. Null prompts interactively.
    public ulong? GuildId { get; init; }
    // Discord user id to grant Admin. Null prompts interactively.
    public ulong? AdminDiscordId { get; init; }
    // Skip the wait for a site login. Set for CI and scripted runs so they cannot hang.
    public bool NoWait { get; init; }

    // Parses setup flags out of the process args. Throws ArgumentException for a malformed or unrecognised
    // flag: a mistyped "--admin" would otherwise be dropped in silence, and a scripted run would then skip
    // the grant, exit 0 and report success without anyone having become Admin. "--onboard" stays accepted
    // as an explicit no-op, since running this project is itself the setup command and developers may still
    // have the old invocation in muscle memory.
    public static OnboardOptions Parse(string[] args) {
        ulong? guildId = null;
        ulong? adminId = null;
        var noWait = false;

        for(var i = 0; i < args.Length; i++) {
            switch(args[i]) {
                case "--no-wait":
                    noWait = true;
                    break;
                case "--guild":
                    guildId = ReadUlong(args, ref i, "--guild");
                    break;
                case "--admin":
                    adminId = ReadUlong(args, ref i, "--admin");
                    break;
                case "--onboard":
                    break;
                default:
                    if(args[i].StartsWith('-')) {
                        throw new ArgumentException(
                            $"Unrecognised option '{args[i]}'. Valid options are --guild <id>, --admin <id> and --no-wait.");
                    }
                    break;
            }
        }

        return new OnboardOptions { GuildId = guildId, AdminDiscordId = adminId, NoWait = noWait };
    }

    private static ulong ReadUlong(string[] args, ref int i, string flag) {
        if(i + 1 >= args.Length) {
            throw new ArgumentException($"{flag} requires a numeric Discord id, for example {flag} 123456789012345678.");
        }
        var raw = args[++i];
        if(!ulong.TryParse(raw, out var value)) {
            throw new ArgumentException($"{flag} expects a numeric Discord snowflake, got '{raw}'.");
        }
        return value;
    }
}

// Shared state for one onboard run. Console I/O is injected rather than called directly so steps can be
// tested without a terminal.
public sealed class OnboardContext {
    public required IConfiguration Configuration { get; init; }
    public required OnboardOptions Options { get; init; }
    public required IDbContextFactory<ApplicationDbContext> DbFactory { get; init; }
    public required DiscordSocketClient Discord { get; init; }
    // Provides RoleManager and UserManager to the Identity steps.
    public required IServiceProvider Services { get; init; }
    public required TextWriter Output { get; init; }
    // Reads one line of operator input. Returns null when input is unavailable.
    public required Func<string> ReadLine { get; init; }

    // Written by the guild selection step, read by the guild row step.
    public ulong SelectedGuildId { get; set; }
    // Written by the guild selection step, read by the guild row step.
    public string SelectedGuildName { get; set; } = "";
}

// One setup concern. Implementations must be idempotent.
public interface IOnboardStep {
    // Label shown in the report, for example "Guild row".
    string Name { get; }
    Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken);
}

// Role names onboard manages. Lives here rather than on a step so that the step which creates the role and
// the step which grants it can be written independently of each other.
public static class OnboardRoles {
    public const string Admin = "Admin";
}
