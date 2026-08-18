using EGG9000.Common.Setup;
using Microsoft.Extensions.Configuration.UserSecrets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Onboarding.Steps;

// Adds missing configuration keys to a secrets.json document as empty strings, so an operator can open the
// file and fill in values instead of having to know the key names. Pure text in, text out, so it is
// testable without touching disk. Never modifies or removes a key that is already present: this file holds
// real credentials and setup must not be able to destroy them. Placeholders are empty strings on purpose.
// Any non-empty placeholder would satisfy the preflight's IsNullOrWhiteSpace check and report an unfilled
// key as configured.
public static class SecretsFileScaffolder {
    // Json is the updated document, only meaningful when writing is safe.
    // Added lists the keys given an empty-string entry.
    // Blocked lists keys that could not be added because an ancestor segment holds something other than an
    // object, for example a flat "ConnectionStrings": "..." string. Kept separate from "already there"
    // because the operator has to fix these by hand.
    // ContainsComments means the source had JSON comments. The configuration provider tolerates them but
    // JsonNode cannot round-trip them, so rewriting the file would silently delete them.
    public sealed record ScaffoldOutcome(
        string Json,
        IReadOnlyList<string> Added,
        IReadOnlyList<string> Blocked,
        bool ContainsComments);

    // Matches what Microsoft.Extensions.Configuration.Json actually accepts. Parsing more strictly
    // than the application does would reject a secrets.json that works perfectly well at runtime.
    private static readonly JsonDocumentOptions ParseOptions = new() {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // Returns existingJson with an empty-string entry added for every key in configKeys that it does not
    // already contain. Colons in a key nest it, so "ConnectionStrings:Token" becomes { "ConnectionStrings":
    // { "Token": "" } }, matching the shape the README has always shown.
    // Throws JsonException when the existing document is not valid JSON, in which case nothing is written.
    public static ScaffoldOutcome AddMissingKeys(string existingJson, IEnumerable<string> configKeys) {
        var root = string.IsNullOrWhiteSpace(existingJson)
            ? new JsonObject()
            : JsonNode.Parse(existingJson, documentOptions: ParseOptions) as JsonObject
                ?? throw new JsonException("secrets.json must contain a JSON object at the top level.");

        var added = new List<string>();
        var blocked = new List<string>();
        foreach(var key in configKeys) {
            switch(TryAddKey(root, key)) {
                case AddResult.Added: added.Add(key); break;
                case AddResult.PathBlocked: blocked.Add(key); break;
            }
        }

        return new ScaffoldOutcome(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            added,
            blocked,
            ContainsComments(existingJson));
    }

    // Sets configKey to value only when it is currently absent or empty. An existing non-empty value is
    // left alone, keeping the same "never destroy what the operator put there" guarantee as AddMissingKeys.
    // Throws JsonException when the existing document is not valid JSON, in which case nothing is written.
    public static ScaffoldOutcome SetIfAbsent(string existingJson, string configKey, string value) {
        var root = string.IsNullOrWhiteSpace(existingJson)
            ? new JsonObject()
            : JsonNode.Parse(existingJson, documentOptions: ParseOptions) as JsonObject
                ?? throw new JsonException("secrets.json must contain a JSON object at the top level.");

        var added = new List<string>();
        var blocked = new List<string>();
        switch(TryAddKey(root, configKey)) {
            case AddResult.Added:
                SetLeaf(root, configKey, value);
                added.Add(configKey);
                break;
            case AddResult.AlreadyPresent:
                // Present but empty counts as unset, which is exactly what AddMissingKeys leaves behind.
                if(string.IsNullOrWhiteSpace(ReadLeaf(root, configKey))) {
                    SetLeaf(root, configKey, value);
                    added.Add(configKey);
                }
                break;
            case AddResult.PathBlocked:
                blocked.Add(configKey);
                break;
        }

        return new ScaffoldOutcome(
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            added,
            blocked,
            ContainsComments(existingJson));
    }

    private static JsonObject Parent(JsonObject root, string configKey) {
        var segments = configKey.Split(':');
        var node = root;
        for(var i = 0; i < segments.Length - 1; i++) {
            node = (JsonObject)node[segments[i]];
        }
        return node;
    }

    // Reads and writes go through whichever spelling the file already uses, so a flat-form file stays
    // flat rather than growing a nested duplicate of the same key.
    private static void SetLeaf(JsonObject root, string configKey, string value) {
        if(root.ContainsKey(configKey)) {
            root[configKey] = value;
            return;
        }
        Parent(root, configKey)[configKey.Split(':')[^1]] = value;
    }

    // Not GetValue<string>: the value may legitimately be a JSON number if someone wrote the id
    // unquoted, and that must count as "already set" rather than throwing.
    private static string ReadLeaf(JsonObject root, string configKey) {
        var node = root.ContainsKey(configKey)
            ? root[configKey]
            : Parent(root, configKey)[configKey.Split(':')[^1]];
        if(node is null) return null;
        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToJsonString();
    }

    // True when the document contains a JSON comment. Detected separately from parsing because the parse
    // deliberately skips comments, and a skipped comment is one the rewrite would destroy.
    public static bool ContainsComments(string json) {
        if(string.IsNullOrWhiteSpace(json)) return false;

        var reader = new Utf8JsonReader(
            Encoding.UTF8.GetBytes(json),
            new JsonReaderOptions { CommentHandling = JsonCommentHandling.Allow, AllowTrailingCommas = true });
        try {
            while(reader.Read()) {
                if(reader.TokenType == JsonTokenType.Comment) return true;
            }
        } catch(JsonException) {
            // Malformed input is the caller's problem to report; it is not a comment.
            return false;
        }
        return false;
    }

    private enum AddResult { Added, AlreadyPresent, PathBlocked }

    private static AddResult TryAddKey(JsonObject root, string configKey) {
        // secrets.json may spell a key flat, as a single "ConnectionStrings:Token" property, which
        // IConfiguration accepts just as readily as the nested form. Adding a nested twin alongside it
        // makes the whole file unloadable ("A duplicate key 'ConnectionStrings:Token' was found"), which
        // would break the bot and the site at startup, so a flat key counts as already present.
        if(root.ContainsKey(configKey)) return AddResult.AlreadyPresent;

        var segments = configKey.Split(':');
        var node = root;

        // Walk to the parent of the leaf, creating containers as needed. A segment already holding
        // a non-object (someone used a flat "A:B" key) is left alone rather than overwritten.
        for(var i = 0; i < segments.Length - 1; i++) {
            if(!node.TryGetPropertyValue(segments[i], out var child)) {
                var created = new JsonObject();
                node[segments[i]] = created;
                node = created;
                continue;
            }
            if(child is not JsonObject childObject) return AddResult.PathBlocked;
            node = childObject;
        }

        var leaf = segments[^1];
        if(node.ContainsKey(leaf)) return AddResult.AlreadyPresent;

        node[leaf] = "";
        return AddResult.Added;
    }
}

// Verifies every configuration key this solution needs is resolvable before any database work starts, and
// scaffolds the ones that are missing into secrets.json. Reports all missing keys at once rather than
// failing on the first, so the operator fixes secrets.json in a single pass. Reads RequiredConfig, so a new
// secret never leaves setup documentation stale. This is the only thing that tells a new developer which
// keys exist: the README deliberately no longer lists them.
public sealed class SecretsPreflightStep : IOnboardStep {
    private readonly string _userSecretsId;
    private readonly Func<string, string> _readFile;
    private readonly Action<string, string> _writeFile;

    // userSecretsId: Overrides the id read from the entry assembly. Tests supply this; production does not,
    // because the id differs per build configuration and hardcoding one names the wrong file under the
    // others. readFile: Reads a secrets file, returning null when absent. Injected for tests. writeFile:
    // Writes a secrets file, creating parent directories. Injected for tests.
    public SecretsPreflightStep(
        string userSecretsId = null,
        Func<string, string> readFile = null,
        Action<string, string> writeFile = null) {

        _userSecretsId = userSecretsId ?? ResolveUserSecretsId();
        _readFile = readFile ?? DefaultRead;
        _writeFile = writeFile ?? DefaultWrite;
    }

    public string Name => "Secrets preflight";

    // The UserSecretsId of the running build, which varies by configuration. Read at runtime rather than
    // hardcoded so the path printed to the operator is the file the process actually reads.
    public static string ResolveUserSecretsId() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;

    public Task<OnboardResult> RunAsync(OnboardContext context, CancellationToken cancellationToken) {
        // Setup refuses to run under RELEASE and DEV9001, so release-only keys are never required here.
        // Both components are checked, not just the bot: the site's OAuth keys are needed before the
        // developer can log in, which the admin grant step below then waits for.
        var missingBot = RequiredConfig.MissingFor(context.Configuration, ConfigComponent.Bot, isRelease: false);
        var missingSite = RequiredConfig.MissingFor(context.Configuration, ConfigComponent.Site, isRelease: false);

        var missing = missingBot
            .Concat(missingSite)
            .DistinctBy(e => e.ConfigKey)
            .ToList();

        if(missing.Count == 0) {
            return Task.FromResult(OnboardResult.AlreadyExisted("all required keys present"));
        }

        context.Output.WriteLine();
        context.Output.WriteLine($"  Missing {missing.Count} required configuration key(s):");
        context.Output.WriteLine();
        foreach(var entry in missing) {
            var scope = missingBot.Any(b => b.ConfigKey == entry.ConfigKey) ? "needed now" : "needed by the website";
            context.Output.WriteLine($"    {entry.ConfigKey}   ({scope})");
            context.Output.WriteLine($"        {entry.Purpose}");
            if(entry.DockerSecretName is not null) {
                context.Output.WriteLine($"        docker secret: {entry.DockerSecretName}");
            }
        }
        context.Output.WriteLine();

        // Only the bot's own keys block setup. Missing website keys are reported and scaffolded but
        // do not stop the run, because the steps below need neither of them.
        var blocking = missingBot.Count > 0;
        var wrote = Scaffold(context, missing.Select(e => e.ConfigKey).ToList(), blocking);

        if(blocking) {
            return Task.FromResult(OnboardResult.Failed($"{missingBot.Count} key(s) missing"));
        }
        // Created only when a file was actually written. Every path that declines to write reports
        // Skipped, so "created" never appears against a run that changed nothing.
        var detail = $"{missing.Count} website key(s) still to fill in";
        return Task.FromResult(wrote ? OnboardResult.Created(detail) : OnboardResult.Skipped(detail));
    }

    // blocking: Whether the run is about to stop. Only then is "run setup again" correct advice; when just
    // the website keys are missing the run carries on and finishes, so telling the operator to re-run would
    // contradict the closing message. Returns true when the secrets file was actually written.
    private bool Scaffold(OnboardContext context, IReadOnlyList<string> configKeys, bool blocking) {
        if(_userSecretsId is null) {
            context.Output.WriteLine("  Could not determine this build's UserSecretsId, so no file was written.");
            context.Output.WriteLine();
            return false;
        }

        var path = RequiredConfig.UserSecretsPathHint(_userSecretsId);

        string existing;
        SecretsFileScaffolder.ScaffoldOutcome outcome;
        try {
            existing = _readFile(path);
            outcome = SecretsFileScaffolder.AddMissingKeys(existing ?? "", configKeys);
        } catch(JsonException ex) {
            // Never overwrite a file that failed to parse. It may hold real credentials.
            ByHand(context, path, $"could not be read as JSON, so it was left untouched: {ex.Message}");
            return false;
        } catch(Exception ex) {
            // A locked or unreadable file is the operator's to resolve. Reporting it here keeps it a
            // readable message rather than an exception surfacing as a failed step.
            ByHand(context, path, $"could not be read: {ex.Message}");
            return false;
        }

        // Checked before the comment refusal below: when there is nothing to write, the file is not
        // at risk, so refusing to touch it and telling the operator to add keys by hand would be
        // both pointless and wrong, since the keys are already there.
        if(outcome.Added.Count == 0 && outcome.Blocked.Count == 0) {
            context.Output.WriteLine("  The keys are already present but empty in:");
            context.Output.WriteLine($"    {path}");
            context.Output.WriteLine();
            context.Output.WriteLine(FillInAdvice(blocking));
            context.Output.WriteLine();
            return false;
        }

        // JsonNode cannot round-trip comments, so rewriting would silently delete them. The file
        // holds real credentials and may well carry notes about them; dropping those is not a
        // trade setup gets to make on the operator's behalf.
        if(outcome.ContainsComments) {
            ByHand(context, path, "contains comments, which a rewrite would delete, so it was left untouched.");
            return false;
        }

        var wrote = false;
        if(outcome.Added.Count > 0) {
            try {
                _writeFile(path, outcome.Json);
            } catch(Exception ex) {
                ByHand(context, path, $"could not be written: {ex.Message}");
                return false;
            }
            wrote = true;
            // Saying "created" matters: the operator was never told to make this file, so naming it as
            // new is the only signal that setup produced it rather than found it.
            context.Output.WriteLine(existing is null
                ? $"  Created your secrets file with {outcome.Added.Count} empty key(s):"
                : $"  Added {outcome.Added.Count} empty key(s) to:");
        } else {
            context.Output.WriteLine("  No keys could be added to:");
        }
        context.Output.WriteLine($"    {path}");

        if(outcome.Blocked.Count > 0) {
            context.Output.WriteLine();
            context.Output.WriteLine("  These could not be added, because a parent name already holds a non-object value:");
            foreach(var key in outcome.Blocked) {
                context.Output.WriteLine($"    {key}");
            }
            context.Output.WriteLine("  Fix that entry by hand.");
        }

        context.Output.WriteLine();
        context.Output.WriteLine(FillInAdvice(blocking));
        context.Output.WriteLine();
        return wrote;
    }

    private static string FillInAdvice(bool blocking) => blocking
        ? "  Fill in the values there, then run setup again."
        : "  Fill in the values there before starting the website.";

    private static void ByHand(OnboardContext context, string path, string reason) {
        context.Output.WriteLine($"  {path}");
        context.Output.WriteLine($"  {reason}");
        context.Output.WriteLine("  Add the keys listed above to it by hand.");
        context.Output.WriteLine();
    }

    private static string DefaultRead(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

    private static void DefaultWrite(string path, string contents) {
        var directory = Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, contents);
    }
}
