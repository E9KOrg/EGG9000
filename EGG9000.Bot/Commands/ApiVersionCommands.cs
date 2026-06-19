using EGG9000.Common.Commands;
using EGG9000.Common.Consumers;
using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Services;

using MassTransit;

using Microsoft.Extensions.DependencyInjection;

using System;
using System.Threading.Tasks;

using static EGG9000.Common.Helpers.Discord.EmbedHelpers;

namespace EGG9000.Bot.Commands {
    public static class ApiVersionCommands {

        [SlashCommand(Description = "Update the Egg Inc API version triple at runtime (validated against the live API first).", AdminOnly = StaffOnlyLevel.Admin, ParentCommand = "b")]
        public static async Task SetVersions(
            FauxCommand command,
            IServiceProvider provider,
            [SlashParam(Description = "Numeric client version (e.g. 72)")] int clientVersion,
            [SlashParam(Description = "App version string (e.g. 1.35.6)")] string appVersion,
            [SlashParam(Description = "App build string (e.g. 1.35.6.3)")] string appBuild) {

            await command.DeferAsync(ephemeral: true);

            if(clientVersion <= 0 || string.IsNullOrWhiteSpace(appVersion) || string.IsNullOrWhiteSpace(appBuild)) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError("clientVersion must be positive and appVersion/appBuild must be non-empty."); });
                return;
            }

            var valid = await EggIncApi.ValidateVersionsAsync((uint)clientVersion, appVersion, appBuild);
            if(!valid) {
                await command.ModifyOriginalResponseAsync(x => { x.Content = ""; x.Embed = EmbedError($"Rejected `{clientVersion} / {appVersion} / {appBuild}`: the API returned no contracts (bad/stale/typo'd version, or a transient failure). Nothing changed."); });
                return;
            }

            var oldTriple = $"{EggIncApi.ClientVersion} / {EggIncApi.AppVersion} / {EggIncApi.AppBuild}";
            EggIncApi.SetVersions((uint)clientVersion, appVersion, appBuild);

            var publisher = provider.GetService<IPublishEndpoint>();
            var scope = "this instance only (broadcast unavailable)";
            if(publisher is not null) {
                await publisher.Publish(new UpdateApiVersionsMessage { ClientVersion = (uint)clientVersion, AppVersion = appVersion, AppBuild = appBuild });
                scope = "all running instances";
            }

            await command.ModifyOriginalResponseAsync(x => {
                x.Content = "";
                x.Embed = EmbedSuccess($"API versions updated and applied to {scope}.\n**Old:** `{oldTriple}`\n**New:** `{clientVersion} / {appVersion} / {appBuild}`");
            });
        }
    }
}
