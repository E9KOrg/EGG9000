using EGG9000.Common.EggIncAPI;

using Microsoft.Extensions.Logging;

using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public sealed record UpdateApiVersionsMessage(uint ClientVersion, string AppVersion, string AppBuild);

    public sealed class UpdateApiVersionsHandler(ILogger<UpdateApiVersionsHandler> logger) : IBusHandler<UpdateApiVersionsMessage> {
        public Task HandleAsync(UpdateApiVersionsMessage m, CancellationToken ct) {
            EggIncApi.SetVersions(m.ClientVersion, m.AppVersion, m.AppBuild);
            logger.LogInformation("[ApiVersions] Applied client={client} version={version} build={build}", m.ClientVersion, m.AppVersion, m.AppBuild);
            return Task.CompletedTask;
        }
    }
}
