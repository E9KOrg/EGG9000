using EGG9000.Common.Helpers;
using EGG9000.Common.Setup;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Records the selected server as this instance's dev server, by writing KnownGuilds.DevGuildConfigKey into
// secrets.json. Without this, code paths that look up the dev guild (FAQ topics, event customizations,
// rankup messages, the site's DebugLogin) fall back to the shared E9K dev server, which a developer running
// their own bot in their own server is not in. Those lookups use FirstAsync and would throw on a database
// that has no row for it. The operator already chose their server in the selection step, so asking them to
// copy the id into a config key by hand would reintroduce exactly the manual step this command exists to
// remove.
public sealed class DevGuildStep : IOnboardStep {
    private readonly string _userSecretsId;
    private readonly Func<string, string> _readFile;
    private readonly Action<string, string> _writeFile;

    public DevGuildStep(
        string userSecretsId = null,
        Func<string, string> readFile = null,
        Action<string, string> writeFile = null) {

        _userSecretsId = userSecretsId ?? SecretsPreflightStep.ResolveUserSecretsId();
        _readFile = readFile ?? (path => File.Exists(path) ? File.ReadAllText(path) : null);
        _writeFile = writeFile ?? WriteCreatingDirectory;
    }

    public string Name => "Dev server id";

    public Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        if(context.SelectedGuildId == 0) {
            return Task.FromResult(OnboardResult.Skipped("no server selected"));
        }

        if(_userSecretsId is null) {
            return Task.FromResult(OnboardResult.Skipped("could not determine this build's UserSecretsId"));
        }

        var path = RequiredConfig.UserSecretsPathHint(_userSecretsId);
        var value = context.SelectedGuildId.ToString();

        SecretsFileScaffolder.ScaffoldOutcome outcome;
        try {
            outcome = SecretsFileScaffolder.SetIfAbsent(_readFile(path) ?? "", KnownGuilds.DevGuildConfigKey, value);
        } catch(JsonException ex) {
            return Task.FromResult(OnboardResult.Skipped($"secrets.json is not valid JSON, set {KnownGuilds.DevGuildConfigKey} by hand: {ex.Message}"));
        } catch(Exception ex) {
            return Task.FromResult(OnboardResult.Skipped($"could not read secrets.json, set {KnownGuilds.DevGuildConfigKey} by hand: {ex.Message}"));
        }

        if(outcome.Blocked.Count > 0) {
            return Task.FromResult(OnboardResult.Skipped(
                $"a parent name in secrets.json holds a non-object value, set {KnownGuilds.DevGuildConfigKey} by hand"));
        }

        if(outcome.Added.Count == 0) {
            // Already set to something. Never overwritten, because the operator may deliberately be
            // pointing at a different server from the one they are seeding right now.
            return Task.FromResult(OnboardResult.AlreadyExisted($"{KnownGuilds.DevGuildConfigKey} is already set"));
        }

        if(outcome.ContainsComments) {
            return Task.FromResult(OnboardResult.Skipped(
                $"secrets.json has comments a rewrite would delete, set {KnownGuilds.DevGuildConfigKey} to {value} by hand"));
        }

        try {
            _writeFile(path, outcome.Json);
        } catch(Exception ex) {
            return Task.FromResult(OnboardResult.Skipped($"could not write secrets.json, set {KnownGuilds.DevGuildConfigKey} to {value} by hand: {ex.Message}"));
        }

        return Task.FromResult(OnboardResult.Created($"{KnownGuilds.DevGuildConfigKey} = {value}"));
    }

    private static void WriteCreatingDirectory(string path, string contents) {
        var directory = Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, contents);
    }
}
