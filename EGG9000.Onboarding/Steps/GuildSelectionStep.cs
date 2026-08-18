using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Id: Discord guild snowflake. Name: Guild display name.
public sealed record GuildChoice(ulong Id, string Name);

// Picks which guild to seed. Split out from the step so the branching is unit-testable without a live
// Discord gateway connection.
public static class GuildSelector {
    // Resolves the guild to seed. A supplied requestedId is validated against the available list and never
    // prompts. A single available guild is auto-selected. Otherwise the operator is prompted until they
    // give a valid index. allowPrompt: False for scripted runs, which must fail fast rather than block on
    // input that will never arrive. Passing --no-wait sets this, so the flag covers this prompt as well as
    // the login wait. Throws ArgumentException: No guilds available, requestedId not among them, or input
    // unavailable.
    public static GuildChoice Select(
        IReadOnlyList<GuildChoice> available, ulong? requestedId, Func<string> readLine, TextWriter output, bool allowPrompt = true) {
        if(available.Count == 0) {
            throw new ArgumentException(
                "The bot is not in any Discord servers. Invite it to your test server, then run setup again.");
        }

        if(requestedId is ulong wanted) {
            var match = available.FirstOrDefault(g => g.Id == wanted);
            if(match is null) {
                var names = string.Join(", ", available.Select(g => $"{g.Name} ({g.Id})"));
                throw new ArgumentException($"The bot is not in guild {wanted}. It is in: {names}");
            }
            return match;
        }

        if(available.Count == 1) {
            output.WriteLine($"  Only one server available, selecting {available[0].Name} ({available[0].Id}).");
            return available[0];
        }

        if(!allowPrompt) {
            var choices = string.Join(", ", available.Select(g => $"{g.Name} ({g.Id})"));
            throw new ArgumentException(
                $"More than one server is available and this run cannot prompt. Re-run with --guild <id>. Available: {choices}");
        }

        output.WriteLine();
        output.WriteLine("  Servers your bot is in:");
        for(var i = 0; i < available.Count; i++) {
            output.WriteLine($"    {i + 1}) {available[i].Name}  {available[i].Id}");
        }
        output.WriteLine();

        while(true) {
            output.Write($"  Pick [1-{available.Count}]: ");
            var answer = readLine();
            if(answer is null) {
                throw new ArgumentException(
                    $"No input available to choose a server. Re-run with --guild <id>, for example --guild {available[0].Id}.");
            }
            if(int.TryParse(answer.Trim(), out var index) && index >= 1 && index <= available.Count) {
                return available[index - 1];
            }
            output.WriteLine($"  '{answer.Trim()}' is not a choice between 1 and {available.Count}.");
        }
    }
}

// Resolves which Discord server to seed and records it on the context for the guild row step. The Discord
// client is already logged in by the orchestrator before any step runs.
public sealed class GuildSelectionStep : IOnboardStep {
    public string Name => "Guild selection";

    public Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        var available = context.Discord.Guilds
            .Select(g => new GuildChoice(g.Id, g.Name))
            .OrderBy(g => g.Name)
            .ToList();

        var allowPrompt = !context.Options.NoWait && !Console.IsInputRedirected;
        var choice = GuildSelector.Select(available, context.Options.GuildId, context.ReadLine, context.Output, allowPrompt);
        context.SelectedGuildId = choice.Id;
        context.SelectedGuildName = choice.Name;

        return Task.FromResult(OnboardResult.AlreadyExisted($"{choice.Name} ({choice.Id})"));
    }
}
