using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Common.Bus {
    public sealed class BusDispatcher(IEnumerable<BusRoute> routes, IServiceScopeFactory scopeFactory, ILogger<BusDispatcher> logger) {
        private readonly ILookup<string, BusRoute> _routes = routes.ToLookup(r => r.Type);

        public async Task DispatchAsync(BusEnvelope envelope, CancellationToken ct) {
            foreach(var route in _routes[envelope.Type]) {
                try {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await route.Invoke(scope.ServiceProvider, envelope.Payload, ct);
                } catch(OperationCanceledException) when(ct.IsCancellationRequested) {
                    throw;
                } catch(Exception e) {
                    logger.LogError(e, "bus handler for {Type} failed", envelope.Type);
                }
            }
        }
    }
}
