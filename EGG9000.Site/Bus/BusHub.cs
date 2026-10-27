using EGG9000.Common.Bus;
using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using System.Threading.Tasks;

namespace EGG9000.Site.Bus {
    [AllowAnonymous]
    public sealed class BusHub(BusDispatcher dispatcher, ILogger<BusHub> logger) : Hub {
        private bool IsBot => SecretsHelper.IsValidBotKey(Context.GetHttpContext()?.Request.Headers[BusEnvelope.AuthHeader]);

        public override Task OnConnectedAsync() {
            if(!IsBot) {
                logger.LogWarning("Rejected bus connection {ConnectionId} with a missing or invalid key", Context.ConnectionId);
                Context.Abort();
            }
            return base.OnConnectedAsync();
        }

        public async Task Publish(BusEnvelope envelope) {
            if(!IsBot) {
                Context.Abort();
                return;
            }
            await Clients.Others.SendAsync(BusEnvelope.ReceiveMethod, envelope, Context.ConnectionAborted);
            await dispatcher.DispatchAsync(envelope, Context.ConnectionAborted);
        }
    }

    public sealed class HubMessageBus(IHubContext<BusHub> hub) : IMessageBus {
        public Task PublishAsync<T>(T message, System.Threading.CancellationToken ct = default) where T : class =>
            hub.Clients.All.SendAsync(BusEnvelope.ReceiveMethod, BusEnvelope.Wrap(message), ct);
    }
}
