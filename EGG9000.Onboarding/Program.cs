using EGG9000.Onboarding;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;

// Entry point for first-time setup. This is a standalone console app rather than a mode of
// EGG9000.Bot because onboard spans both halves of the product: it seeds the bot's Guilds row and
// grants the site's Admin identity role. Neither project owns both, and keeping it separate also
// keeps setup code out of the published bot image.
// User secrets first, environment variables second, so the environment wins. The reverse order
// (which EGG9000.Bot uses) is unsafe here: this command writes empty-string placeholders into the
// secrets file, and an empty string is a value rather than an absence. It would therefore shadow a
// working ConnectionStrings__DefaultConnection from the environment, and every later run would
// report that key as missing and refuse to start.
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true) // Optional for Docker scenarios
    .AddEnvironmentVariables()
    .Build();

EGG9000.Common.Helpers.KnownGuilds.Initialize(configuration);

// AdminGrantStep can wait several minutes for a site login and tells the operator Ctrl-C stops it.
// Cancelling the token rather than killing the process lets that step report Skipped and exit 0,
// so the run always ends on a command the operator can simply repeat.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    cancellation.Cancel();
};

return await OnboardCommand.RunAsync(args, configuration, cancellation.Token);
