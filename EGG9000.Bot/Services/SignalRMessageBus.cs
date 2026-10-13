using EGG9000.Common.Bus;
using EGG9000.Common.Helpers;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace EGG9000.Bot.Services {
    public sealed class SignalRMessageBus : BackgroundService, IMessageBus {
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

        private readonly HubConnection _connection;
        private readonly ILogger<SignalRMessageBus> _logger;

        public SignalRMessageBus(BusDispatcher dispatcher, ILogger<SignalRMessageBus> logger) {
            _logger = logger;
            _connection = new HubConnectionBuilder()
                .WithUrl(SiteApiClient.BaseUrl() + BusEnvelope.HubPath, o => o.Headers[BusEnvelope.AuthHeader] = SecretsHelper.BotToken)
                .WithAutomaticReconnect(new ForeverRetryPolicy())
                .Build();
            _connection.On<BusEnvelope>(BusEnvelope.ReceiveMethod, envelope => dispatcher.DispatchAsync(envelope, CancellationToken.None));
            _connection.Reconnected += _ => {
                _logger.LogInformation("bus reconnected to the site");
                return Task.CompletedTask;
            };
            _connection.Closed += e => {
                _logger.LogWarning(e, "bus connection to the site closed");
                return Task.CompletedTask;
            };
        }

        public async Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class {
            if(_connection.State != HubConnectionState.Connected) {
                _logger.LogWarning("bus not connected ({State}), dropping {Type}", _connection.State, typeof(T).Name);
                return;
            }
            try {
                await _connection.InvokeAsync(BusEnvelope.PublishMethod, BusEnvelope.Wrap(message), ct);
            } catch(Exception e) when(e is not OperationCanceledException) {
                _logger.LogWarning(e, "bus publish of {Type} failed", typeof(T).Name);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
            while(!stoppingToken.IsCancellationRequested) {
                try {
                    await _connection.StartAsync(stoppingToken);
                    _logger.LogInformation("bus connected to the site");
                    return;
                } catch(Exception e) when(!stoppingToken.IsCancellationRequested) {
                    _logger.LogWarning("bus connect to the site failed, retrying in {Delay}: {Message}", RetryDelay, e.Message);
                    await Task.Delay(RetryDelay, stoppingToken);
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken) {
            await base.StopAsync(cancellationToken);
            await _connection.DisposeAsync();
        }

        private sealed class ForeverRetryPolicy : IRetryPolicy {
            public TimeSpan? NextRetryDelay(RetryContext retryContext) => retryContext.PreviousRetryCount < 5 ? TimeSpan.FromSeconds(2) : RetryDelay;
        }
    }
}
