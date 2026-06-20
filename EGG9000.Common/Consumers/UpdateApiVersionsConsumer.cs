using EGG9000.Common.EggIncAPI;
using EGG9000.Common.Helpers;

using MassTransit;

using Microsoft.Extensions.Logging;

using System.Threading.Tasks;

namespace EGG9000.Common.Consumers {
    public class UpdateApiVersionsConsumer(ILogger<UpdateApiVersionsConsumer> logger) : IConsumer<UpdateApiVersionsMessage> {
        private readonly ILogger<UpdateApiVersionsConsumer> _logger = logger;

        public Task Consume(ConsumeContext<UpdateApiVersionsMessage> context) {
            var m = context.Message;
            if(!SecretsHelper.IsValidBusSecret(m.Secret)) {
                _logger.LogWarning("Rejected UpdateApiVersionsMessage with invalid control secret");
                return Task.CompletedTask;
            }
            EggIncApi.SetVersions(m.ClientVersion, m.AppVersion, m.AppBuild);
            _logger.LogInformation("[ApiVersions] Applied client={client} version={version} build={build}", m.ClientVersion, m.AppVersion, m.AppBuild);
            return Task.CompletedTask;
        }
    }

    public class UpdateApiVersionsMessage {
        public uint ClientVersion { get; set; }
        public string AppVersion { get; set; }
        public string AppBuild { get; set; }
        public string Secret { get; set; } = SecretsHelper.BusControlSecret;
    }
}
